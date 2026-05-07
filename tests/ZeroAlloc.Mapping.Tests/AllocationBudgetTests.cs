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
}
