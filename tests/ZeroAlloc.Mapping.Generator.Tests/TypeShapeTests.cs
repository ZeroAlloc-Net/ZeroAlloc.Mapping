using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// A mapping works on any type an attribute can name: a nested type, a constructed generic, a
/// generic over a nested type, a type named with a verbatim identifier, or a built-in type. The
/// generator used to resolve each type again from its display string, which is not a metadata
/// name, and skipped every mapping it failed to find with no diagnostic (#136). A mapping that
/// really cannot be generated reports ZAMP023.
/// </summary>
public class TypeShapeTests
{
    private const string Types = """
        using ZeroAlloc.Mapping;
        namespace App
        {
            public static class Dto
            {
                public sealed record A(int X);
                public sealed record B(int X);
            }
            public sealed record G<T>(T X);
            public sealed record H(int X);
            public sealed record @event(int X);
            public sealed record @class(int X);
        }
        """;

    [Theory]
    [InlineData("Map<Dto.A, Dto.B>", "Map(new Dto.A(1)).X")]
    [InlineData("Map<G<int>, H>", "Map(new G<int>(1)).X")]
    [InlineData("Map<G<Dto.A>, G<Dto.B>>", "Map(new G<Dto.A>(new Dto.A(1))).X.X")]
    [InlineData("Map<@event, @class>", "Map(new @event(1)).X")]
    [InlineData("TryMap<Dto.A, Dto.B>", "TryMap(new Dto.A(1)).Value.X")]
    [InlineData("TryMap<G<int>, H>", "TryMap(new G<int>(1)).Value.X")]
    [InlineData("TryMap<G<Dto.A>, G<Dto.A>>", "TryMap(new G<Dto.A>(new Dto.A(1))).Value.X.X")]
    [InlineData("TryMap<@event, @class>", "TryMap(new @event(1)).Value.X")]
    public void Mapping_IsGenerated_AndCompiles(string attribute, string call)
    {
        // G<Dto.A> to G<Dto.B> needs the nested Dto.A to Dto.B mapping as well.
        var nested = attribute == "Map<G<Dto.A>, G<Dto.B>>" ? "[Map<Dto.A, Dto.B>]" : "";
        var run = TestHarness.RunGeneratorAndCompile(Types + $$"""
            namespace App
            {
                [{{attribute}}]
                {{nested}}
                public static partial class M { }
                public static class Calls
                {
                    public static int Run() => M.{{call}};
                }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Single(run.HintNames);
        Assert.Empty(run.Errors);
    }

    private const string EveryFeatureSource = """
        using ZeroAlloc.Mapping;
        using System.Collections.Generic;
        namespace App
        {
            public static class Dto
            {
                public sealed record A(int X);
                public sealed record B(int X);
                public abstract class Shape { }
                public sealed class Circle : Shape { public int R { get; set; } }
                public abstract class ShapeDto { }
                public sealed class CircleDto : ShapeDto { public int R { get; set; } }
                public sealed class Node { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                public sealed class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                public sealed class Bag { public List<Tag>? Tags { get; set; } }
                public sealed class BagCopy { public List<Tag>? Tags { get; set; } }
                public sealed class Tag { public string Name { get; set; } = ""; }
                public sealed class Mutable { public int X { get; set; } }
                public sealed class MutableDto { public int X { get; set; } }
            }

            [ReverseMap<Dto.A, Dto.B>]
            public static partial class Reverse { }

            [Map<Dto.Circle, Dto.CircleDto>]
            [PolymorphicMap<Dto.Shape, Dto.ShapeDto>]
            public static partial class Poly { }

            [Map<Dto.Node, Dto.NodeDto>(CycleSafe = true)]
            public static partial class Cycle { }

            [Map<Dto.Bag, Dto.BagCopy>(DeepClone = true)]
            public static partial class Clone { }

            [Map<Dto.A, Dto.B>(Projection = true)]
            public static partial class Projected { }

            [Map<Dto.Mutable, Dto.MutableDto>]
            public static partial class InPlace
            {
                public static partial void Map(Dto.Mutable src, Dto.MutableDto existingDst);
            }

            public static class Calls
            {
                public static object[] Run() => new object[]
                {
                    Reverse.Map(new Dto.A(1)),
                    Reverse.Map(new Dto.B(1)),
                    Poly.Map((Dto.Shape)new Dto.Circle()),
                    Poly.Map(new List<Dto.Shape>()),
                    Cycle.Map(new Dto.Node()),
                    Clone.Map(new Dto.Bag()),
                    Projected.Projection,
                    Projected.Map(new List<Dto.A>()),
                };
            }
        }
        """;

    /// <summary>
    /// Every feature that reads a mapping's types reads them from the same symbols: reverse
    /// mappings, polymorphic dispatch, cycle-safe and deep-clone mappings, projections,
    /// update-in-place and collection overloads.
    /// </summary>
    [Fact]
    public void EveryFeature_WorksOnNestedTypes()
    {
        var run = TestHarness.RunGeneratorAndCompile(EveryFeatureSource);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(6, run.HintNames.Count);
        Assert.Empty(run.Errors);
    }

    /// <summary>
    /// The per-mapping checks run on nested types too. ZAMP001 used to be skipped along with
    /// the mapping.
    /// </summary>
    [Fact]
    public void Diagnostics_AreReported_ForNestedTypes()
    {
        var diagnostics = TestHarness.RunDiagnostics("""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public static class Dto
                {
                    public sealed record A(int X);
                    public sealed record B(int X, int Y);
                }
                [Map<Dto.A, Dto.B>]
                public static partial class M { }
            }
            """);

        var zamp001 = Assert.Single(diagnostics, d => d.Id == "ZAMP001");
        Assert.Contains("'Y' on 'App.Dto.B'", zamp001.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Map")]
    [InlineData("TryMap")]
    public void DestinationWithoutPublicConstructor_ReportsZamp023(string kind)
    {
        var source = $$"""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public interface IB { int X { get; } }
                [{{kind}}<A, IB>]
                public static partial class M { }
            }
            """;

        var zamp023 = Assert.Single(TestHarness.RunDiagnostics(source), d => d.Id == "ZAMP023");

        Assert.Equal(DiagnosticSeverity.Warning, zamp023.Severity);
        Assert.Equal(
            "The mapping from 'App.A' to 'App.IB' is not generated because 'App.IB' has no public constructor",
            zamp023.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var span = zamp023.Location.SourceSpan;
        Assert.Equal($"{kind}<A, IB>", source.Substring(span.Start, span.Length));
    }

    [Theory]
    [InlineData("Map<int[], App.A>", "'int[]'")]
    [InlineData("TryMap<App.A, int[]>", "'int[]'")]
    [InlineData("ReverseMap<int[], App.A>", "'int[]'")]
    [InlineData("PolymorphicMap<App.A, int[]>", "'int[]'")]
    public void TypeThatIsNotAClassOrStruct_ReportsZamp023(string attribute, string named)
    {
        var source = $$"""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                [{{attribute}}]
                public static partial class M { }
            }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);

        var zamp023 = Assert.Single(run.GeneratorDiagnostics, d => d.Id == "ZAMP023");
        Assert.Contains(
            named + " is not a class, struct, record or interface",
            zamp023.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        var span = zamp023.Location.SourceSpan;
        Assert.Equal(attribute, source.Substring(span.Start, span.Length));
        Assert.Empty(run.Errors);
    }

    /// <summary>
    /// A type the compiler cannot bind already has a compiler error. The generator adds nothing.
    /// </summary>
    [Fact]
    public void UnboundType_ReportsOnlyTheCompilerError()
    {
        var diagnostics = TestHarness.RunDiagnostics("""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                [Map<A, Missing>]
                public static partial class M { }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("ZAMP", StringComparison.Ordinal));
    }
}
