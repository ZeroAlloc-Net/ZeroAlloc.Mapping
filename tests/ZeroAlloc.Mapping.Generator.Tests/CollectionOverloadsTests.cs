using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class CollectionOverloadsTests
{
    [Fact]
    public Task Map_AutoEmits_List_Overload()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void SkipCollectionOverloads_Emits_Only_Single()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            [SkipCollectionOverloads]
            public static partial class M { }
            """;
        var output = TestHarness.RunGenerator(source);
        Assert.DoesNotContain("List<global::Dst> Map(", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Map(global::Src[] src)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("IEnumerable<global::Dst> Map(", output, StringComparison.Ordinal);
        Assert.Contains("public static global::Dst Map(global::Src src)", output, StringComparison.Ordinal);
    }

    [Fact]
    public Task PolymorphicMap_Gets_CollectionOverloads_Too()
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
}
