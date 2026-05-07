using ZeroAlloc.Mapping;
using ZeroAlloc.Mapping.AotSmoke.Internal;
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
