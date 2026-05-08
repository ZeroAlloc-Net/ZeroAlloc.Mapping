using ZeroAlloc.Mapping.AotSmoke.Internal;

namespace ZeroAlloc.Mapping.Tests;

public sealed record OrderRequest(int Id, string Notes);
public sealed record Order(int Id, string Notes);

public readonly record struct BudgetEmail
{
    public BudgetEmail(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("empty", nameof(value));
        Value = value;
    }
    public string Value { get; }
}

public sealed record SignUpRequest(string Email);
public sealed record SignedUpUser(BudgetEmail Email);

[Map<OrderRequest, Order>]
[TryMap<SignUpRequest, SignedUpUser>]
public static partial class BudgetMappings
{
    // No-op hooks — fire only on the [Map<OrderRequest, Order>] generated body.
    // Empty bodies must not introduce per-call allocations.
    [BeforeMap]
    public static void NoopBefore(OrderRequest src) { }

    [AfterMap]
    public static void NoopAfter(OrderRequest src, Order dst) { }
}

// Sibling fixture: ReverseMap symmetry.
public sealed record BudgetFwd(int X);
public sealed record BudgetRev(int X);

[ReverseMap<BudgetFwd, BudgetRev>]
public static partial class BudgetReverseFixtures { }

// Sibling fixture: dotted-path flatten.
public sealed record BudgetInner(int Value);
public sealed record BudgetOuter(BudgetInner Nested);
public sealed record BudgetFlat(int Value);

[Map<BudgetOuter, BudgetFlat>]
public static partial class BudgetFlattenFixtures
{
    [MapProperty("Nested.Value", "Value")]
    public static partial BudgetFlat Map(BudgetOuter src);
}

// Sibling fixture: update-in-place void overload (B5).
public sealed class BudgetUpdateDst
{
    public int Id { get; set; }
    public string Notes { get; set; } = "";
}
public sealed record BudgetUpdateSrc(int Id, string Notes);

[Map<BudgetUpdateSrc, BudgetUpdateDst>]
public static partial class BudgetUpdateFixtures
{
    public static partial void Map(BudgetUpdateSrc src, BudgetUpdateDst existingDst);
}

// Sibling fixture: culture-aware parsing (B9).
public sealed record BudgetCultureSrc(string Amount);
public sealed record BudgetCultureDst(decimal Amount);

[Map<BudgetCultureSrc, BudgetCultureDst>]
[MappingCulture("nl-NL")]
public static partial class BudgetCultureFixtures { }

// Sibling fixture: polymorphic dispatch (B2). Derived types redeclare their
// own primary-ctor parameter (no base-record inheritance) so the generator's
// declared-only member scan picks up the property on each derived DTO.
public abstract record BudgetAnimal;
public sealed record BudgetCat(string Name) : BudgetAnimal;
public sealed record BudgetDog(string Name) : BudgetAnimal;
public abstract record BudgetAnimalDto;
public sealed record BudgetCatDto(string Name) : BudgetAnimalDto;
public sealed record BudgetDogDto(string Name) : BudgetAnimalDto;

[Map<BudgetCat, BudgetCatDto>]
[Map<BudgetDog, BudgetDogDto>]
[PolymorphicMap<BudgetAnimal, BudgetAnimalDto>]
public static partial class BudgetPolymorphicFixtures { }

public class AllocationBudgetTests
{
    // ---- 3 self-tests of the gate ----

    [Fact]
    public void Gate_DetectsAllocation_WhenActionAllocates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudget(
                budgetBytes: 0,
                iterations: 1000,
                action: () => _ = new object(),
                label: "test-allocator"));

        Assert.Contains("test-allocator", ex.Message, StringComparison.Ordinal);
        Assert.Contains("budget is 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_TolerantOfWarmupOnlyAllocations()
    {
        var firstCall = true;
        AllocationGate.AssertBudget(0, 1000, () =>
        {
            if (firstCall) { firstCall = false; _ = new object(); }
        }, "warmup-only-allocator");
    }

    [Fact]
    public void Gate_RejectsZeroIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AllocationGate.AssertBudget(0, 0, () => { }, "zero-iter"));
    }

    // ---- 5 mapping-budget tests ----

    [Fact]
    public void Map_OrderRequestToOrder_FlatRecord_WithinBudget()
    {
        var req = new OrderRequest(42, "n");
        AllocationGate.AssertBudget(80, 1000, () => _ = BudgetMappings.Map(req), "[Map<OrderRequest, Order>]");
    }

    [Fact]
    public void TryMap_HappyPath_WithinBudget()
    {
        var req = new SignUpRequest("user@example.com");
        AllocationGate.AssertBudget(120, 1000, () => _ = BudgetMappings.TryMap(req), "[TryMap] happy");
    }

    [Fact]
    public void TryMap_DenyPath_WithinBudget()
    {
        var req = new SignUpRequest("");
        // Deny path throws + catches an Exception → stack-trace string + Exception instance dominate.
        // 1 KB/call is a generous-but-tight cap; ensures we don't regress to e.g. 10 KB.
        AllocationGate.AssertBudget(1024, 1000, () => _ = BudgetMappings.TryMap(req), "[TryMap] deny");
    }

    [Fact]
    public void Map_HappyPath_RepeatedCall_WithinBudget()
    {
        var req = new OrderRequest(1, "x");
        AllocationGate.AssertBudget(80, 5000, () => _ = BudgetMappings.Map(req), "[Map] repeated");
    }

    [Fact]
    public void TryMap_HappyPath_RepeatedCall_WithinBudget()
    {
        var req = new SignUpRequest("a@b.com");
        AllocationGate.AssertBudget(120, 5000, () => _ = BudgetMappings.TryMap(req), "[TryMap] repeated happy");
    }

    // ---- 3 v1-extension feature budgets ----

    [Fact]
    public void Map_With_BeforeAfterHooks_WithinBudget()
    {
        var req = new OrderRequest(1, "n");
        AllocationGate.AssertBudget(80, 1000, () => _ = BudgetMappings.Map(req), "[Map] with hooks");
    }

    [Fact]
    public void Map_Reverse_WithinBudget()
    {
        var fwd = new BudgetFwd(7);
        AllocationGate.AssertBudget(80, 1000, () => _ = BudgetReverseFixtures.Map(fwd), "[ReverseMap] forward");
    }

    [Fact]
    public void Map_With_Flattening_WithinBudget()
    {
        var outer = new BudgetOuter(new BudgetInner(42));
        AllocationGate.AssertBudget(80, 1000, () => _ = BudgetFlattenFixtures.Map(outer), "[MapProperty] flatten");
    }

    // ---- 3 v1.2-extension feature budgets ----

    [Fact]
    public void Map_UpdateInPlace_WithinBudget()
    {
        var src = new BudgetUpdateSrc(1, "n");
        var dst = new BudgetUpdateDst { Id = 0, Notes = "" };
        AllocationGate.AssertBudget(80, 1000, () => BudgetUpdateFixtures.Map(src, dst), "[Map] update-in-place");
    }

    [Fact]
    public void Map_With_MappingCulture_WithinBudget()
    {
        var src = new BudgetCultureSrc("12,34");
        // Budget loosened from the standard 80 B/call to 256 B/call: BCL
        // `decimal.Parse(string, IFormatProvider)` allocates ~160 B/call for the
        // internal NumberBuffer + parse state when given a non-Invariant culture
        // (CultureInfo.GetCultureInfo itself is cached, but the parse path is not
        // alloc-free for `decimal`). The destination record itself is ~24 B; the
        // remainder is BCL overhead inherent to `decimal.Parse` with a culture.
        AllocationGate.AssertBudget(256, 1000, () => _ = BudgetCultureFixtures.Map(src), "[Map] with culture");
    }

    [Fact]
    public void Map_Polymorphic_Dispatch_WithinBudget()
    {
        BudgetAnimal cat = new BudgetCat("Whiskers");
        AllocationGate.AssertBudget(96, 1000, () => _ = BudgetPolymorphicFixtures.Map(cat), "[PolymorphicMap] dispatch");
    }

    [Fact]
    public void Map_List_Overload_WithinBudget()
    {
        // 50-element list. Baseline: ~24 B/dst record × 50 = 1200 B; +List<T> internal
        // T[] backing array (50 × 8 ref + 24 header ≈ 424 B) + List<T> object header
        // (~40 B) + per-call closure/state ≈ ~2056 B/call measured on .NET 10.
        // Budget raised from 2048 → 2200 to accommodate the backing-array overhead
        // that the original estimate underweighted (List<T>(capacity) allocates the
        // backing T[] eagerly, not just the List wrapper).
        var src = new System.Collections.Generic.List<OrderRequest>();
        for (int i = 0; i < 50; i++) src.Add(new OrderRequest(i, "n"));
        AllocationGate.AssertBudget(2200, 200, () => _ = BudgetMappings.Map(src), "[Map] List<T> overload (50 elements)");
    }

    [Fact]
    public void Map_IEnumerable_Overload_WithinBudget_Lazy()
    {
        // IEnumerable overload returns Enumerable.Select — zero iteration at call time.
        // Measured ~72 B/call on .NET 10: the SelectIListIterator<TSource, TResult>
        // class itself is ~56 B (header + 3 fields + a Func<,> reference), and the
        // captured Func<OrderRequest, Order> delegate adds another ~16 B in some
        // codepaths. Budget raised from 64 → 96 (50% headroom over measured).
        var src = new System.Collections.Generic.List<OrderRequest>();
        for (int i = 0; i < 50; i++) src.Add(new OrderRequest(i, "n"));
        AllocationGate.AssertBudget(96, 1000,
            () => _ = BudgetMappings.Map((System.Collections.Generic.IEnumerable<OrderRequest>)src),
            "[Map] IEnumerable<T> overload (lazy, no enumeration)");
    }
}
