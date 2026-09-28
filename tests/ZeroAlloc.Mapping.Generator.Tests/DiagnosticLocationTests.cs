using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// Every rule points at the declaration it is about: the mapping attribute that asked for the
/// mapping, the attribute on the user partial that breaks it, or the class that cannot host it.
/// A location on the class identifier made every mapping of a class share one location, so a
/// <c>#pragma warning disable</c> could not silence one mapping without silencing the others.
/// </summary>
public class DiagnosticLocationTests
{
    [Fact]
    public void ZAMP001_DestinationHasNoSource_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP001", """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            public static partial class M { }
            """, "Map<Src, Dst>");

    [Fact]
    public void ZAMP002_NoConversionPath_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP002", """
            using ZeroAlloc.Mapping;
            public sealed class Foo { }
            public sealed class Bar { }
            public sealed record Src(Foo X);
            public sealed record Dst(Bar X);
            [Map<Src, Dst>]
            public static partial class M { }
            """, "Map<Src, Dst>");

    [Fact]
    public void ZAMP003_AmbiguousSource_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP003", """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X, int Other);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Other", "X")]
                public static partial Dst Map(Src src);
            }
            """, "MapProperty(\"Other\", \"X\")");

    [Fact]
    public void ZAMP004_MapChainsTryMap_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP004", """
            using ZeroAlloc.Mapping;
            public sealed record Inner1(int X);
            public sealed record Inner2(int X);
            public sealed record Outer1(Inner1 Child);
            public sealed record Outer2(Inner2 Child);
            [Map<Outer1, Outer2>]
            [TryMap<Inner1, Inner2>]
            public static partial class M { }
            """, "Map<Outer1, Outer2>");

    [Fact]
    public void ZAMP005_MapPropertyTargetMissing_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP005", """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("DoesNotExist", "X")]
                public static partial Dst Map(Src src);
            }
            """, "MapProperty(\"DoesNotExist\", \"X\")");

    [Fact]
    public void ZAMP006_NotStaticPartialClass_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP006", """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public class M { }
            """, "M");

    [Fact]
    public void ZAMP007_NullableMismatch_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP007", """
            #nullable enable
            using ZeroAlloc.Mapping;
            public sealed record Src(string? Name);
            public sealed record Dst(string Name);
            [Map<Src, Dst>]
            public static partial class M { }
            """, "Map<Src, Dst>");

    [Fact]
    public void ZAMP008_AmbiguousConstructor_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP008", """
            using ZeroAlloc.Mapping;
            public sealed class Dst
            {
                public Dst(int X, string Y) { }
                public Dst(int X, int Y) { }
            }
            public sealed record Src(int X, string Y);
            [Map<Src, Dst>]
            public static partial class M { }
            """, "Map<Src, Dst>");

    [Fact]
    public void ZAMP009_ReverseMapNotSymmetric_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP009", """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Foo);
            public sealed record Dst(string Bar);
            [ReverseMap<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Foo", "Bar")]
                public static partial Dst Map(Src src);
            }
            """, "MapProperty(\"Foo\", \"Bar\")");

    [Fact]
    public void ZAMP010_UnconsumedSource_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP010", """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B);
            public sealed record Dst(int A);
            [Map<Src, Dst>]
            [StrictSourceMapping]
            public static partial class M { }
            """, "Map<Src, Dst>");

    [Fact]
    public void ZAMP011_CaseInsensitiveAmbiguous_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP011", """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Foo, string foo);
            public sealed record Dst(string FOO);
            [Map<Src, Dst>]
            [CaseInsensitiveMapping]
            public static partial class M { }
            """, "Map<Src, Dst>");

    [Fact]
    public void ZAMP012_UpdateInPlaceNotSettable_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP012", """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>]
            public static partial class M
            {
                public static partial void Map(Src src, Dst existingDst);
            }
            """, "Map<Src, Dst>");

    [Fact]
    public void ZAMP013_PolymorphicNoCases_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP013", """
            using ZeroAlloc.Mapping;
            public abstract record Animal(string Name);
            public abstract record AnimalDto(string Name);
            [PolymorphicMap<Animal, AnimalDto>]
            public static partial class M { }
            """, "PolymorphicMap<Animal, AnimalDto>");

    [Fact]
    public void ZAMP014_PolymorphicSealedBase_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP014", """
            using ZeroAlloc.Mapping;
            public sealed record Cat(string Name);
            public sealed record CatDto(string Name);
            [Map<Cat, CatDto>]
            [PolymorphicMap<Cat, CatDto>]
            public static partial class M { }
            """, "PolymorphicMap<Cat, CatDto>");

    [Fact]
    public void ZAMP015_PolymorphicMixedKinds_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP015", """
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
            """, "PolymorphicMap<Animal, AnimalDto>");

    [Fact]
    public void ZAMP016_DuplicateMappingCulture_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP016", """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            [MappingCulture("nl-NL")]
            public static partial class M { }
            [MappingCulture("en-US")]
            public static partial class M { }
            """, "MappingCulture(\"en-US\")");

    [Fact]
    public void ZAMP017_ProjectionIncompatibleFeature_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP017", """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>(Projection = true)]
            public static partial class M
            {
                [BeforeMap]
                public static void Before(Src s) { /* unused */ }
            }
            """, "Map<Src, Dst>(Projection = true)");

    [Fact]
    public void ZAMP018_CycleSafeMissingOnNested_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP018", """
            using ZeroAlloc.Mapping;
            public sealed record CustomerSrc(string Name);
            public sealed record CustomerDst(string Name);
            public sealed record OrderSrc(int Id, CustomerSrc? Customer);
            public sealed record OrderDst(int Id, CustomerDst? Customer);
            [Map<OrderSrc, OrderDst>(CycleSafe = true)]
            [Map<CustomerSrc, CustomerDst>]
            public static partial class M { }
            """, "Map<OrderSrc, OrderDst>(CycleSafe = true)");

    [Fact]
    public void ZAMP019_DeepCloneUncloneableType_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP019", """
            using ZeroAlloc.Mapping;
            public sealed class Unreachable { private Unreachable() { } public string Name { get; } = ""; }
            public sealed class Src { public int Id { get; set; } public Unreachable? Lookup { get; set; } }
            public sealed class Dst { public int Id { get; set; } public Unreachable? Lookup { get; set; } }
            [Map<Src, Dst>(DeepClone = true)]
            public static partial class M { }
            """, "Map<Src, Dst>(DeepClone = true)");

    [Fact]
    public void ZAMP020_DeepCloneCyclicTypeGraph_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP020", """
            using ZeroAlloc.Mapping;
            public sealed class Node { public string Name { get; set; } = ""; public Node? Self { get; set; } }
            [Map<Node, Node>(DeepClone = true)]
            public static partial class M { }
            """, "Map<Node, Node>(DeepClone = true)");

    [Fact]
    public void ZAMP021_DeepCloneCycleSafePrimaryCtorCycle_points_at_its_declaration() =>
        AssertLocatedAt("ZAMP021", """
            using ZeroAlloc.Mapping;
            public sealed record Node(string Name, Node? Next);
            [Map<Node, Node>(DeepClone = true, CycleSafe = true)]
            public static partial class Mappers { }
            """, "Map<Node, Node>(DeepClone = true, CycleSafe = true)");

    private static void AssertLocatedAt(string id, string source, string expectedText)
    {
        var diagnostic = Assert.Single(TestHarness.RunDiagnostics(source), d => d.Id == id);

        Assert.True(diagnostic.Location.IsInSource, $"{id} has no source location");
        Assert.Equal(expectedText, LocatedText(diagnostic));
    }

    /// <summary>
    /// A mapper compiled into a referenced library keeps its [Map] attributes in metadata and has
    /// no declaring syntax. It is that library's mapper, not a host this project got wrong.
    /// </summary>
    [Fact]
    public void ZAMP006_ignores_mapper_classes_in_referenced_assemblies()
    {
        var library = CSharpCompilation.Create(
            "MapperLibrary",
            [CSharpSyntaxTree.ParseText("""
                using ZeroAlloc.Mapping;
                public sealed record Src(int A);
                public sealed record Dst(int A);
                [Map<Src, Dst>]
                public static partial class LibraryMappers { }
                """)],
            TestHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver libraryDriver = CSharpGeneratorDriver.Create(new MappingGenerator());
        libraryDriver.RunGeneratorsAndUpdateCompilation(library, out var generatedLibrary, out _);
        using var image = new MemoryStream();
        Assert.True(generatedLibrary.Emit(image).Success);
        image.Position = 0;

        var consumer = CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText("public class Uses { public Dst Run(Src s) => LibraryMappers.Map(s); }")],
            TestHarness.References().Append(MetadataReference.CreateFromStream(image)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new MappingGenerator())
            .RunGeneratorsAndUpdateCompilation(consumer, out _, out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Id == "ZAMP006");
    }

    [Fact]
    public void Pragma_silences_one_mapping_and_not_its_sibling()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record QuietSrc(int A);
            public sealed record QuietDst(int A, int B);
            public sealed record LoudSrc(int A);
            public sealed record LoudDst(int A, int B);
            #pragma warning disable ZAMP001
            [Map<QuietSrc, QuietDst>]
            #pragma warning restore ZAMP001
            [Map<LoudSrc, LoudDst>]
            public static partial class M { }
            """;

        var compilation = Compile(CSharpSyntaxTree.ParseText(source, path: "/src/Mappers.cs"));
        CSharpGeneratorDriver.Create(new MappingGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var zamp001 = diagnostics.Where(d => d.Id == "ZAMP001").ToList();
        Assert.Equal(2, zamp001.Count);
        Assert.True(Assert.Single(zamp001, d => d.GetMessage().Contains("QuietDst", StringComparison.Ordinal)).IsSuppressed);
        Assert.False(Assert.Single(zamp001, d => d.GetMessage().Contains("LoudDst", StringComparison.Ordinal)).IsSuppressed);
    }

    /// <summary>
    /// The generator re-runs on every compilation, so nothing holds a location from an earlier
    /// run. This pins that: after an edit to an unrelated file, and after an edit that moves the
    /// mapping down its own file, each diagnostic points into the current compilation's tree.
    /// </summary>
    [Fact]
    public void Locations_bind_to_the_current_compilation_after_edits()
    {
        var mappers = CSharpSyntaxTree.ParseText("""
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            public static partial class M { }
            """, path: "/src/Mappers.cs");
        var other = CSharpSyntaxTree.ParseText("public class Other { }", path: "/src/Other.cs");
        var compilation = Compile(mappers, other);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MappingGenerator());
        driver = driver.RunGenerators(compilation);
        var first = Assert.Single(Diagnostics(driver), d => d.Id == "ZAMP001");
        Assert.Same(mappers, first.Location.SourceTree);

        var editedOther = other.WithChangedText(other.GetText().Replace(0, 0, "// edited\n"));
        compilation = compilation.ReplaceSyntaxTree(other, editedOther);
        driver = driver.RunGenerators(compilation);
        var afterUnrelatedEdit = Assert.Single(Diagnostics(driver), d => d.Id == "ZAMP001");
        Assert.Same(mappers, afterUnrelatedEdit.Location.SourceTree);
        Assert.Equal(first.Location.SourceSpan, afterUnrelatedEdit.Location.SourceSpan);

        var movedMappers = mappers.WithChangedText(mappers.GetText().Replace(0, 0, "// one\n// two\n"));
        compilation = compilation.ReplaceSyntaxTree(mappers, movedMappers);
        driver = driver.RunGenerators(compilation);
        var afterOwnEdit = Assert.Single(Diagnostics(driver), d => d.Id == "ZAMP001");
        Assert.Same(movedMappers, afterOwnEdit.Location.SourceTree);
        Assert.Equal(
            first.Location.GetLineSpan().StartLinePosition.Line + 2,
            afterOwnEdit.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal("Map<Src, Dst>", LocatedText(afterOwnEdit));
    }

    private static string LocatedText(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static IEnumerable<Diagnostic> Diagnostics(GeneratorDriver driver) =>
        driver.GetRunResult().Results.SelectMany(r => r.Diagnostics);

    private static CSharpCompilation Compile(params SyntaxTree[] trees) =>
        CSharpCompilation.Create(
            "TestCompilation",
            trees,
            TestHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
