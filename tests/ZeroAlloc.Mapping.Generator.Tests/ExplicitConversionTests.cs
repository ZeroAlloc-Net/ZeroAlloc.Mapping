using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// Only implicit conversions are applied. An explicit one can throw or lose data, so it is
/// reported as ZAMP002 and the member is not emitted, instead of being emitted without a cast
/// and failing the build with CS1503 (#127).
/// </summary>
public class ExplicitConversionTests
{
    public static TheoryData<string, string, string> Cases() => new()
    {
        { "Map", "int?", "int" },
        { "Map", "ProbeA", "ProbeB" },
        { "Map", "long", "int" },
        { "TryMap", "int?", "int" },
        { "TryMap", "ProbeA", "ProbeB" },
        { "TryMap", "long", "int" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitConversion_IsReportedAsZamp002_NamingTheConversion(string attribute, string from, string to)
    {
        var diagnostics = TestHarness.RunDiagnostics(Source(attribute, from, to));

        var zamp002 = Assert.Single(diagnostics, d => d.Id == "ZAMP002");
        var message = zamp002.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"explicit conversion from '{from}' to '{to}'", message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitConversion_IsNotEmitted_SoNoCs1503Follows(string attribute, string from, string to)
    {
        var output = TestHarness.RunGenerator(Source(attribute, from, to));
        Assert.DoesNotContain("V: src.V", output, StringComparison.Ordinal);
        Assert.Contains("Ok: src.Ok", output, StringComparison.Ordinal);

        var errors = CompileWithGenerator(Source(attribute, from, to))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.DoesNotContain(errors, d => d.Id == "CS1503");
    }

    [Fact]
    public void ImplicitConversion_IsStillApplied()
    {
        var source = Source("Map", "int", "long");
        Assert.DoesNotContain(TestHarness.RunDiagnostics(source), d => d.Id == "ZAMP002");
        Assert.Contains("V: src.V", TestHarness.RunGenerator(source), StringComparison.Ordinal);
        Assert.DoesNotContain(CompileWithGenerator(source), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrelatedTypes_KeepTheNoConversionMessage()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed class Foo { }
            public sealed class Bar { }
            public sealed record Src(Foo X);
            public sealed record Dst(Bar X);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var zamp002 = Assert.Single(TestHarness.RunDiagnostics(source), d => d.Id == "ZAMP002");
        var message = zamp002.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("no implicit conversion", message, StringComparison.Ordinal);
        Assert.DoesNotContain("explicit conversion from", message, StringComparison.Ordinal);
    }

    private static string Source(string attribute, string from, string to) => $$"""
        using ZeroAlloc.Mapping;
        public enum ProbeA { X, Y }
        public enum ProbeB { X, Y }
        public sealed record Src({{from}} V, string Ok);
        public sealed record Dst({{to}} V, string Ok);
        [{{attribute}}<Src, Dst>]
        public static partial class M { }
        """;

    private static IReadOnlyList<Diagnostic> CompileWithGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            TestHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver.Create(new MappingGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output.GetDiagnostics();
    }
}
