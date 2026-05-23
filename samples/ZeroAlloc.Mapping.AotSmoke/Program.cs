using ZeroAlloc.Mapping;
using ZeroAlloc.TestHelpers;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mapping.AotSmoke;

public sealed record OrderRequest(int Id, string Notes);
public sealed record Order(int Id, string Notes);

public readonly record struct Email
{
    public Email(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("empty", nameof(value));
        Value = value;
    }
    public string Value { get; }
}

public sealed record SignUpRequest(string Email);
public sealed record User(Email Email);

[Map<OrderRequest, Order>]
[TryMap<SignUpRequest, User>]
public static partial class Mappings
{
    // No-op hooks — fire on every generated mapping where the source matches.
    // Here only Mappings.Map(OrderRequest) → Order matches; TryMap source is SignUpRequest.
    [BeforeMap]
    public static void NoopBefore(OrderRequest src) { }

    [AfterMap]
    public static void NoopAfter(OrderRequest src, Order dst) { }
}

// Reverse-map fixture — emits both FwdDto→RevDto and RevDto→FwdDto.
public sealed record FwdDto(int X);
public sealed record RevDto(int X);

[ReverseMap<FwdDto, RevDto>]
public static partial class ReverseFixtures { }

// Flatten fixture — dotted source path collapses Outer.Nested.Value → Flat.Value.
public sealed record Inner(int Value);
public sealed record Outer(Inner Nested);
public sealed record Flat(int Value);

[Map<Outer, Flat>]
public static partial class FlattenFixtures
{
    [MapProperty("Nested.Value", "Value")]
    public static partial Flat Map(Outer src);
}

// Update-in-place fixture (B5) — void overload mutates an existing destination instance.
public sealed class MutableOrder
{
    public int Id { get; set; }
    public string Notes { get; set; } = "";
}
public sealed record MutableOrderRequest(int Id, string Notes);

[Map<MutableOrderRequest, MutableOrder>]
public static partial class UpdateInPlaceFixtures
{
    public static partial void Map(MutableOrderRequest src, MutableOrder existingDst);
}

// Culture fixture (B9) — nl-NL parses "12,34" as 12.34m.
public sealed record CulturePriceSrc(string Amount);
public sealed record CulturePriceDst(decimal Amount);

[Map<CulturePriceSrc, CulturePriceDst>]
[MappingCulture("nl-NL")]
public static partial class CultureFixtures { }

// Polymorphic fixture (B2) — base-typed dispatch to derived mappings.
// Derived types redeclare their own primary-ctor parameter so each DTO has
// its own declared `Name` property (the generator only scans declared members).
public abstract record AotAnimal;
public sealed record AotCat(string Name) : AotAnimal;
public sealed record AotDog(string Name) : AotAnimal;

public abstract record AotAnimalDto;
public sealed record AotCatDto(string Name) : AotAnimalDto;
public sealed record AotDogDto(string Name) : AotAnimalDto;

[Map<AotCat, AotCatDto>]
[Map<AotDog, AotDogDto>]
[PolymorphicMap<AotAnimal, AotAnimalDto>]
public static partial class AnimalMappings { }

// v1.4 B3 — Projection fixture (LINQ-to-Objects path; EF Core not available in AOT smoke).
public sealed record ProjSrc(int Id, string Name);
public sealed record ProjDst(int Id, string Name);

[Map<ProjSrc, ProjDst>(Projection = true)]
public static partial class ProjMappings { }

// v1.4 B6 — Cycle-safe self-ref fixture (class with parameterless ctor + settable properties).
public sealed class NodeSrc { public string Name { get; set; } = ""; public NodeSrc? Self { get; set; } }
public sealed class NodeDst { public string Name { get; set; } = ""; public NodeDst? Self { get; set; } }

[Map<NodeSrc, NodeDst>(CycleSafe = true)]
public static partial class NodeMappings { }

// v1.4 B11 — DeepClone fixture.
public sealed class Tag
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

public sealed class DcSrc
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<Tag>? Tags { get; set; }
}

public sealed class DcDst
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<Tag>? Tags { get; set; }
}

[Map<DcSrc, DcDst>(DeepClone = true)]
public static partial class DcMappings { }

public static class Program
{
    public static int Main()
    {
        try
        {
            ExerciseBehavior();
            Console.WriteLine("AOT mapping behavior OK");

            ExerciseAllocationGates();
            Console.WriteLine("AOT allocation gate OK");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AOT smoke FAILED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void ExerciseBehavior()
    {
        // [Map] happy path
        var order = Mappings.Map(new OrderRequest(42, "hello"));
        if (order.Id != 42 || order.Notes != "hello")
            throw new InvalidOperationException("[Map] happy path returned unexpected payload");

        // [TryMap] happy path
        var success = Mappings.TryMap(new SignUpRequest("user@example.com"));
        if (!success.IsSuccess)
            throw new InvalidOperationException("[TryMap] happy path returned failure");

        // [TryMap] deny path — empty email triggers ArgumentException in Email ctor
        var failure = Mappings.TryMap(new SignUpRequest(""));
        if (failure.IsSuccess)
            throw new InvalidOperationException("[TryMap] deny path returned success");
        if (failure.Error.Code != "mapping.constructor.threw")
            throw new InvalidOperationException(
                $"[TryMap] deny path: unexpected error code '{failure.Error.Code}'");

        // ReverseMap — exercise both directions.
        var rev = ReverseFixtures.Map(new FwdDto(1));
        if (rev.X != 1)
            throw new InvalidOperationException("[ReverseMap] forward direction returned unexpected payload");
        var fwd = ReverseFixtures.Map(new RevDto(2));
        if (fwd.X != 2)
            throw new InvalidOperationException("[ReverseMap] reverse direction returned unexpected payload");

        // Flatten — dotted source path.
        var flat = FlattenFixtures.Map(new Outer(new Inner(42)));
        if (flat.Value != 42)
            throw new InvalidOperationException("[MapProperty] flatten returned unexpected payload");

        // B5 update-in-place
        var existing = new MutableOrder { Id = 0, Notes = "stale" };
        UpdateInPlaceFixtures.Map(new MutableOrderRequest(42, "fresh"), existing);
        if (existing.Id != 42 || existing.Notes != "fresh")
            throw new InvalidOperationException("[Map] update-in-place returned unexpected payload");

        // B9 culture
        var price = CultureFixtures.Map(new CulturePriceSrc("12,34"));
        if (price.Amount != 12.34m)
            throw new InvalidOperationException($"[MappingCulture] nl-NL parse returned {price.Amount}, expected 12.34");

        // B2 polymorphic
        AotAnimal cat = new AotCat("Whiskers");
        AotAnimal dog = new AotDog("Rex");
        var catDto = AnimalMappings.Map(cat);
        var dogDto = AnimalMappings.Map(dog);
        if (catDto is not AotCatDto || dogDto is not AotDogDto)
            throw new InvalidOperationException("[PolymorphicMap] dispatched to wrong derived type");

        // B8: collection overloads (auto-emitted on existing Mappings class)
        var orders = Mappings.Map(new System.Collections.Generic.List<OrderRequest>
        {
            new(1, "a"),
            new(2, "b")
        });
        if (orders.Count != 2 || orders[0].Id != 1 || orders[1].Id != 2)
            throw new InvalidOperationException("[Map] List<TSrc> overload returned unexpected payload");

        var orderArr = Mappings.Map(new[] { new OrderRequest(3, "c") });
        if (orderArr.Length != 1 || orderArr[0].Id != 3)
            throw new InvalidOperationException("[Map] TSrc[] overload returned unexpected payload");

        // B3 — Projection. The Projection property holds an Expression<Func<TSrc,TDst>>
        // intended for IQueryable.Select (typically over EF Core, which is not in the
        // AOT smoke). Neither AsQueryable nor Expression.Compile() is AOT-safe
        // (IL2026/IL3050), so we exercise the property as a value: assert it's a
        // well-formed lambda whose body returns the destination type. This proves
        // the generator's Projection emit path survived AOT publish.
        var projExpr = ProjMappings.Projection;
        if (projExpr is null || projExpr.Parameters.Count != 1 || projExpr.ReturnType != typeof(ProjDst))
            throw new InvalidOperationException("[Projection] property shape unexpected");

        // B6 — cycle-safe self-ref
        var node = new NodeSrc { Name = "root" }; node.Self = node;
        var nodeDst = NodeMappings.Map(node);
        if (!ReferenceEquals(nodeDst, nodeDst.Self))
            throw new InvalidOperationException("[CycleSafe] self-cycle did not resolve");

        // B11 — deep-clone independence
        var dcSrc = new DcSrc { OrderId = 1, Tags = new System.Collections.Generic.List<Tag> { new() { Name = "a", Id = 1 } } };
        var dcDst = DcMappings.Map(dcSrc);
        dcSrc.Tags.Add(new() { Name = "z", Id = 99 });
        if (dcDst.Tags?.Count != 1)
            throw new InvalidOperationException("[DeepClone] destination not independent");
    }

    private static void ExerciseAllocationGates()
    {
        // Hold one source instance outside the gate so we measure only the Map call.
        var orderRequest = new OrderRequest(1, "n");
        var signUpOk = new SignUpRequest("user@example.com");

        // Budget: ~24 B for the destination record allocation; loosen for safety.
        AllocationGate.AssertBudget(
            budgetBytes: 80,
            iterations: 1000,
            action: () => { _ = Mappings.Map(orderRequest); },
            label: "[Map<OrderRequest, Order>]");

        AllocationGate.AssertBudget(
            budgetBytes: 80,
            iterations: 1000,
            action: () => { _ = Mappings.TryMap(signUpOk); },
            label: "[TryMap<SignUpRequest, User>] happy path");
    }
}
