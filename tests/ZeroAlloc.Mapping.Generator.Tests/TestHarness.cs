using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZeroAlloc.Mapping;

namespace ZeroAlloc.Mapping.Generator.Tests;

internal static class TestHarness
{
    public static string RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new MappingGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        return string.Join("\n// ===== next file =====\n",
            result.Results
                .SelectMany(r => r.GeneratedSources)
                .Select(s => $"// {s.HintName}\n{s.SourceText}"));
    }

    public static IReadOnlyList<Diagnostic> RunDiagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new MappingGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        return driver.GetRunResult().Results.SelectMany(r => r.Diagnostics).ToList();
    }

    /// <summary>
    /// Runs the generator and compiles its output with the source, keeping the hint names, the
    /// generator's own diagnostics, such as CS8785 when it throws, and the compile errors.
    /// </summary>
    public static GeneratorRun RunGeneratorAndCompile(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new MappingGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var result = driver.GetRunResult();

        return new GeneratorRun(
            result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName).ToList(),
            generatorDiagnostics.ToList(),
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList());
    }

    internal sealed record GeneratorRun(
        IReadOnlyList<string> HintNames,
        IReadOnlyList<Diagnostic> GeneratorDiagnostics,
        IReadOnlyList<Diagnostic> Errors);

    internal static IEnumerable<MetadataReference> References() => ReferenceAssemblies();

    private static IEnumerable<MetadataReference> ReferenceAssemblies()
    {
        var explicitTypes = new[]
        {
            typeof(MappingError),
            typeof(MapAttribute<,>),
            typeof(TryMapAttribute<,>),
            typeof(MapPropertyAttribute),
            typeof(MapValueAttribute),
            typeof(MapperIgnoreSourceAttribute),
            typeof(MapperIgnoreTargetAttribute),
            // [TryMap] returns ZeroAlloc.Results.Result, and a projection is a LINQ expression.
            typeof(ZeroAlloc.Results.Result<,>),
            typeof(System.Linq.Expressions.Expression<>),
        };
        var explicitLocations = explicitTypes
            .Select(t => t.Assembly.Location)
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var domainLocations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location);

        return explicitLocations.Concat(domainLocations)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
    }
}
