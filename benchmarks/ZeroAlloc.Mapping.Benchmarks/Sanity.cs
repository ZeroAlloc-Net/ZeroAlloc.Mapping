using System.Globalization;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks;

/// <summary>
/// Cross-mapper parity sanity check. Runs once at startup before any benchmark
/// numbers are collected — if any mapper produces a structurally different
/// destination from the others, the benchmark numbers downstream are
/// meaningless.
/// </summary>
internal static class Sanity
{
    public static void AssertParity()
    {
        var autoMapper = AutoMapperFactory.Build();

        CheckFlat(autoMapper);
        CheckConv(autoMapper);
        CheckFlatten(autoMapper);
        CheckPolymorphic(autoMapper);
        CheckUpdateInPlace(autoMapper);
        CheckTryMap();
    }

    // ----- Flat -----

    private static void CheckFlat(AutoMapper.IMapper am)
    {
        var src = new FlatSrc(
            Id: 7, Name: "Ada", Email: "ada@example.com", Age: 36,
            Active: true, Score: 99.5, Version: 12345L, Country: "GB");

        var hand = HandWritten.MapFlat(src);
        var za = ZaFlat.Map(src);
        var mapperly = MapperlyFlat.Map(src);
        var auto = am.Map<FlatDst>(src);

        AssertEqual("Flat: HandWritten vs ZA", hand, za);
        AssertEqual("Flat: HandWritten vs Mapperly", hand, mapperly);
        AssertEqual("Flat: HandWritten vs AutoMapper", hand, auto);
    }

    // ----- Conv -----

    private static void CheckConv(AutoMapper.IMapper am)
    {
        var src = new ConvSrc(
            Id: 42,
            Status: nameof(Models.Status.Active),
            Count: 1000,
            // ISO-8601 round-trippable; DateTime.Parse with InvariantCulture
            // produces a Local kind. All four mappers go through the same
            // DateTime.Parse path so they should all match exactly.
            Created: "2026-01-15T10:30:00");

        var hand = HandWritten.MapConv(src);
        var za = ZaConv.Map(src);
        var mapperly = MapperlyConv.Map(src);
        var auto = am.Map<ConvDst>(src);

        AssertEqual("Conv: HandWritten vs ZA", hand, za);
        AssertEqual("Conv: HandWritten vs Mapperly", hand, mapperly);
        AssertEqual("Conv: HandWritten vs AutoMapper", hand, auto);
    }

    // ----- Flatten -----

    private static void CheckFlatten(AutoMapper.IMapper am)
    {
        var src = new OrderSrc(
            OrderId: 1001,
            Customer: new Customer(
                Id: 55,
                Name: "Grace Hopper",
                Address: new Address(Street: "1 Navy Way", City: "Arlington", Zip: "22202")),
            Total: 1234.56m);

        var hand = HandWritten.MapFlatten(src);
        var za = ZaFlatten.Map(src);
        var mapperly = MapperlyFlatten.Map(src);
        var auto = am.Map<OrderFlat>(src);

        AssertEqual("Flatten: HandWritten vs ZA", hand, za);
        AssertEqual("Flatten: HandWritten vs Mapperly", hand, mapperly);
        AssertEqual("Flatten: HandWritten vs AutoMapper", hand, auto);
    }

    // ----- Polymorphic -----

    private static void CheckPolymorphic(AutoMapper.IMapper am)
    {
        // Dog
        Animal dog = new Dog("Rex", "Labrador");
        CheckAnimalCase<DogDto>("Polymorphic[Dog]", dog, am);

        // Cat
        Animal cat = new Cat("Whiskers", Indoor: true);
        CheckAnimalCase<CatDto>("Polymorphic[Cat]", cat, am);

        // Bird
        Animal bird = new Bird("Tweety", Wingspan: 0.25);
        CheckAnimalCase<BirdDto>("Polymorphic[Bird]", bird, am);
    }

    private static void CheckAnimalCase<TExpected>(string label, Animal src, AutoMapper.IMapper am)
        where TExpected : AnimalDto
    {
        var hand = HandWritten.MapAnimal(src);
        var za = ZaPoly.Map(src);
        var mapperly = MapperlyPoly.Map(src);
        var auto = am.Map<AnimalDto>(src);

        if (hand is not TExpected)
            throw new InvalidOperationException(
                $"{label}: HandWritten produced {hand.GetType().Name}, expected {typeof(TExpected).Name}. value={hand}");
        if (za is not TExpected)
            throw new InvalidOperationException(
                $"{label}: ZA produced {za.GetType().Name}, expected {typeof(TExpected).Name}. value={za}");
        if (mapperly is not TExpected)
            throw new InvalidOperationException(
                $"{label}: Mapperly produced {mapperly.GetType().Name}, expected {typeof(TExpected).Name}. value={mapperly}");
        if (auto is not TExpected)
            throw new InvalidOperationException(
                $"{label}: AutoMapper produced {auto.GetType().Name}, expected {typeof(TExpected).Name}. value={auto}");

        // record == is structural; comparing AnimalDto-typed refs still
        // dispatches to the derived record's synthesized Equals.
        AssertEqual($"{label}: HandWritten vs ZA", hand, za);
        AssertEqual($"{label}: HandWritten vs Mapperly", hand, mapperly);
        AssertEqual($"{label}: HandWritten vs AutoMapper", hand, auto);
    }

    // ----- UpdateInPlace -----

    private static void CheckUpdateInPlace(AutoMapper.IMapper am)
    {
        var src = new FlatSrc(
            Id: 7, Name: "Ada", Email: "ada@example.com", Age: 36,
            Active: true, Score: 99.5, Version: 12345L, Country: "GB");

        var hand = new FlatDstMutable();
        HandWritten.UpdateInPlace(src, hand);

        var za = new FlatDstMutable();
        ZaUpdate.Map(src, za);

        var mapperly = new FlatDstMutable();
        MapperlyUpdate.Update(src, mapperly);

        var auto = new FlatDstMutable();
        am.Map(src, auto);

        AssertMutableEqual("UpdateInPlace: HandWritten vs ZA", hand, za);
        AssertMutableEqual("UpdateInPlace: HandWritten vs Mapperly", hand, mapperly);
        AssertMutableEqual("UpdateInPlace: HandWritten vs AutoMapper", hand, auto);
    }

    // ----- TryMap -----

    private static void CheckTryMap()
    {
        var src = new ConvSrc(
            Id: 42,
            Status: nameof(Models.Status.Active),
            Count: 1000,
            Created: "2026-01-15T10:30:00");

        var result = ZaTry.TryMap(src);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"TryMap: happy path returned failure. code={result.Error.Code} path={result.Error.PropertyPath} reason={result.Error.Reason}");
        }

        var direct = ZaConv.Map(src);
        AssertEqual("TryMap: ZaConv.Map vs ZaTry.TryMap.Value", direct, result.Value);
    }

    // ----- helpers -----

    private static void AssertEqual<T>(string label, T expected, T actual) where T : class
    {
        // For records this is structural equality (auto-synthesized Equals).
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: parity mismatch.{Environment.NewLine}  expected: {expected}{Environment.NewLine}  actual:   {actual}");
        }
    }

    private static void AssertMutableEqual(string label, FlatDstMutable expected, FlatDstMutable actual)
    {
        if (expected.Id != actual.Id
            || expected.Name != actual.Name
            || expected.Email != actual.Email
            || expected.Age != actual.Age
            || expected.Active != actual.Active
            || expected.Score != actual.Score
            || expected.Version != actual.Version
            || expected.Country != actual.Country)
        {
            throw new InvalidOperationException(
                $"{label}: parity mismatch.{Environment.NewLine}" +
                $"  expected: {Format(expected)}{Environment.NewLine}" +
                $"  actual:   {Format(actual)}");
        }
    }

    private static string Format(FlatDstMutable m) =>
        $"FlatDstMutable {{ Id = {m.Id}, Name = {m.Name}, Email = {m.Email}, Age = {m.Age}, " +
        $"Active = {m.Active}, Score = {m.Score.ToString(CultureInfo.InvariantCulture)}, " +
        $"Version = {m.Version}, Country = {m.Country} }}";
}
