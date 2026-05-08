using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class PolymorphicMapTests
{
    [Fact]
    public Task PolymorphicMap_Emits_Switch_Dispatcher()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public abstract record Animal(string Name);
            public sealed record Cat(string Name, int Lives) : Animal(Name);
            public sealed record Dog(string Name, string Breed) : Animal(Name);
            public abstract record AnimalDto(string Name);
            public sealed record CatDto(string Name, int Lives) : AnimalDto(Name);
            public sealed record DogDto(string Name, string Breed) : AnimalDto(Name);
            [Map<Cat, CatDto>]
            [Map<Dog, DogDto>]
            [PolymorphicMap<Animal, AnimalDto>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task PolymorphicTryMap_Emits_Result_Returning_Dispatcher()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public abstract record Animal(string Name);
            public sealed record Cat(string Name) : Animal(Name);
            public sealed record Dog(string Name) : Animal(Name);
            public abstract record AnimalDto(string Name);
            public sealed record CatDto(string Name) : AnimalDto(Name);
            public sealed record DogDto(string Name) : AnimalDto(Name);
            [TryMap<Cat, CatDto>]
            [TryMap<Dog, DogDto>]
            [PolymorphicTryMap<Animal, AnimalDto>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task PolymorphicMap_With_Single_Derived_Case_Still_Emits()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public abstract record Animal(string Name);
            public sealed record Cat(string Name) : Animal(Name);
            public abstract record AnimalDto(string Name);
            public sealed record CatDto(string Name) : AnimalDto(Name);
            [Map<Cat, CatDto>]
            [PolymorphicMap<Animal, AnimalDto>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void ZAMP013_PolymorphicMap_With_No_Cases_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public abstract record Animal(string Name);
            public abstract record AnimalDto(string Name);
            [PolymorphicMap<Animal, AnimalDto>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP013");
    }

    [Fact]
    public void ZAMP014_PolymorphicMap_Over_Sealed_Base_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Cat(string Name);
            public sealed record CatDto(string Name);
            [Map<Cat, CatDto>]
            [PolymorphicMap<Cat, CatDto>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP014");
    }

    [Fact]
    public void ZAMP015_PolymorphicMap_Mixes_Map_And_TryMap_Cases_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public abstract record Animal(string Name);
            public sealed record Cat(string Name) : Animal(Name);
            public sealed record Dog(string Name) : Animal(Name);
            public abstract record AnimalDto(string Name);
            public sealed record CatDto(string Name) : AnimalDto(Name);
            public sealed record DogDto(string Name) : AnimalDto(Name);
            [Map<Cat, CatDto>]
            [TryMap<Dog, DogDto>]
            [PolymorphicMap<Animal, AnimalDto>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP015");
    }
}
