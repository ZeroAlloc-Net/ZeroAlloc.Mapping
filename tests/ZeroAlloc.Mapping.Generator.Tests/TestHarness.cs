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
