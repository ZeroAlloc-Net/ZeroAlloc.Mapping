using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// Roslyn compares hint names ignoring case, so two hosts whose qualified names differ only in
/// case would need the same file name. The later host gets ZAMP024 and is not generated; every
/// other host is, under its usual hint name (#135). The generator used to throw instead, and
/// then no mapper in the project was generated.
/// </summary>
public class CaseCollisionTests
{
    [Fact]
    public void HostsDifferingOnlyInCase_ReportZamp024_OnTheLaterHost_AndGenerateTheRest()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                [Map<A, B>] public static partial class M { }
                [Map<A, B>] public static partial class m { }
                [Map<A, B>] public static partial class Other { }
                public static class Calls
                {
                    public static int Run() => M.Map(new A(1)).X + Other.Map(new A(2)).X;
                }
            }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);

        var zamp024 = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZAMP024", zamp024.Id);
        Assert.Equal(DiagnosticSeverity.Error, zamp024.Severity);
        Assert.Equal(
            "Mapper 'App.m' is not generated because its file name 'App.m.g.cs' differs only in case from that of mapper 'App.M'",
            zamp024.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var span = zamp024.Location.SourceSpan;
        Assert.Equal("m", source.Substring(span.Start, span.Length));
        Assert.Equal(new[] { "App.M.g.cs", "App.Other.g.cs" }, run.HintNames.Order(StringComparer.Ordinal));
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void NamespacesDifferingOnlyInCase_Collide_AndEveryLaterHostIsReported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record A(int X);
            public sealed record B(int X);
            namespace App { [Map<A, B>] public static partial class M { } }
            namespace APP { [Map<A, B>] public static partial class M { } }
            namespace app { [Map<A, B>] public static partial class M { } }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);

        Assert.Equal(2, run.GeneratorDiagnostics.Count(d => d.Id == "ZAMP024"));
        Assert.Equal(new[] { "App.M.g.cs" }, run.HintNames);
        Assert.DoesNotContain(run.GeneratorDiagnostics, d => d.Id == "CS8785");
    }

    /// <summary>
    /// The earlier host is the one declared first, by file path and then position, so which host
    /// is generated does not depend on the order the attribute providers run in.
    /// </summary>
    [Fact]
    public void EarlierHost_IsTheOneDeclaredFirst_AcrossFiles()
    {
        var first = CSharpSyntaxTree.ParseText(
            "using ZeroAlloc.Mapping; namespace App; public sealed record A(int X); public sealed record B(int X); [TryMap<A, B>] public static partial class mapper { }",
            path: "a.cs");
        var second = CSharpSyntaxTree.ParseText(
            "using ZeroAlloc.Mapping; namespace App; [ReverseMap<A, B>] public static partial class Mapper { }",
            path: "b.cs");
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { second, first },
            TestHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new MappingGenerator())
            .RunGenerators(compilation)
            .GetRunResult()
            .Results.Single();

        Assert.Equal("App.mapper.g.cs", Assert.Single(result.GeneratedSources).HintName);
        var zamp024 = Assert.Single(result.Diagnostics, d => d.Id == "ZAMP024");
        Assert.Equal("b.cs", zamp024.Location.SourceTree!.FilePath);
    }
}
