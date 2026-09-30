using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// An edit that does not touch a mapper must not make the generator redo its output. The
/// pipeline carries value-equal models, so the second run finds every step unchanged or cached.
/// </summary>
public class IncrementalityTests
{
    private const string Mappers = """
        using ZeroAlloc.Mapping;
        namespace App;
        public sealed record A(int X, string Name);
        public sealed record B(int X, string Name);
        public abstract class Shape { }
        public sealed class Circle : Shape { public int R { get; set; } }
        public abstract class ShapeDto { }
        public sealed class CircleDto : ShapeDto { public int R { get; set; } }
        [TryMap<A, B>]
        [Map<A, B>]
        public static partial class M { }
        [ReverseMap<A, B>]
        public static partial class R { }
        [ReverseTryMap<A, B>]
        public static partial class T { }
        [Map<Circle, CircleDto>]
        [TryMap<Circle, CircleDto>]
        [PolymorphicMap<Shape, ShapeDto>]
        [PolymorphicTryMap<Shape, ShapeDto>]
        public static partial class P { }
        [PolymorphicMap<Shape, ShapeDto>]
        [Map<Circle, CircleDto>]
        public static partial class PM { }
        [PolymorphicTryMap<Shape, ShapeDto>]
        [TryMap<Circle, CircleDto>]
        public static partial class PT { }
        [Map<A, B>]
        public partial class NotStatic { }
        """;

    private static readonly string[] TrackingNames =
    {
        "MapperHosts.Map",
        "MapperHosts.TryMap",
        "MapperHosts.ReverseMap",
        "MapperHosts.ReverseTryMap",
        "MapperHosts.PolymorphicMap",
        "MapperHosts.PolymorphicTryMap",
    };

    [Fact]
    public void UnrelatedEdit_LeavesEveryTrackedStepCachedOrUnchanged()
    {
        var unrelated = CSharpSyntaxTree.ParseText("namespace App; public static class Other { public static int V => 1; }");
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(Mappers), unrelated },
            TestHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MappingGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult().Results.Single();
        Assert.Equal(6, first.GeneratedSources.Length);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id != "ZAMP006");
        Assert.Contains(first.Diagnostics, d => d.Id == "ZAMP006");

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            CSharpSyntaxTree.ParseText("namespace App; public static class Other { public static int V => 2; }"));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results.Single();

        foreach (var name in TrackingNames)
        {
            Assert.True(second.TrackedSteps.ContainsKey(name), $"step {name} is not tracked");
            AssertAllCachedOrUnchanged(name, second.TrackedSteps[name]);
        }

        Assert.NotEmpty(second.TrackedOutputSteps);
        foreach (var (name, steps) in second.TrackedOutputSteps)
            AssertAllCachedOrUnchanged(name, steps);

        Assert.Equal(
            first.GeneratedSources.Select(s => (s.HintName, s.SourceText.ToString())),
            second.GeneratedSources.Select(s => (s.HintName, s.SourceText.ToString())));
        Assert.Equal(
            first.Diagnostics.Select(d => d.ToString()),
            second.Diagnostics.Select(d => d.ToString()));
    }

    [Fact]
    public void EditToAMapper_RegeneratesIt()
    {
        var mappers = CSharpSyntaxTree.ParseText(Mappers);
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { mappers },
            TestHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MappingGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            mappers,
            CSharpSyntaxTree.ParseText(Mappers.Replace("public sealed record B(int X, string Name);", "public sealed record B(long X, string Name);")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results.Single();

        var reasons = second.TrackedOutputSteps
            .SelectMany(kv => kv.Value)
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToList();
        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
    }

    [Fact]
    public void PipelineValues_HoldNoSymbolsSyntaxOrCompilation()
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(Mappers) },
            TestHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MappingGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results.Single();

        var checkedTypes = new HashSet<Type>();
        foreach (var name in TrackingNames)
        {
            foreach (var step in result.TrackedSteps[name])
            {
                foreach (var (value, _) in step.Outputs)
                    AssertValueOnly(value.GetType(), checkedTypes, name);
            }
        }
        Assert.NotEmpty(checkedTypes);
    }

    [Fact]
    public void HostMatchedByManyAttributesAndParts_IsGeneratedAndReportedOnce()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace App;
            public sealed record A(int X);
            public sealed record B(int X);
            public sealed record C(int Y);
            public sealed record D(int Y);
            [TryMap<A, B>]
            public static partial class M { }
            [Map<A, B>]
            [ReverseMap<C, D>]
            public static partial class M { }
            [Map<A, B>]
            public partial class NotStatic { }
            [TryMap<A, B>]
            public partial class NotStatic { }
            """;

        var output = TestHarness.RunGenerator(source);
        var diagnostics = TestHarness.RunDiagnostics(source);

        Assert.Single(output.Split('\n'), l => l == "// M.g.cs");
        Assert.DoesNotContain("NotStatic", output, StringComparison.Ordinal);
        Assert.Single(diagnostics, d => d.Id == "ZAMP006");
    }

    private static void AssertValueOnly(Type type, HashSet<Type> checkedTypes, string step)
    {
        if (!checkedTypes.Add(type)) return;
        Assert.False(
            typeof(ISymbol).IsAssignableFrom(type) || typeof(SyntaxNode).IsAssignableFrom(type) ||
            typeof(Compilation).IsAssignableFrom(type) || typeof(SemanticModel).IsAssignableFrom(type) ||
            typeof(Location).IsAssignableFrom(type) || typeof(Diagnostic).IsAssignableFrom(type),
            $"step {step} carries a {type}");

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                AssertValueOnly(argument, checkedTypes, step);
        }
        if (type.Assembly != typeof(MappingGenerator).Assembly) return;
        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length == 0)
                AssertValueOnly(property.PropertyType, checkedTypes, step);
        }
    }

    private static void AssertAllCachedOrUnchanged(string name, System.Collections.Immutable.ImmutableArray<IncrementalGeneratorRunStep> steps)
    {
        foreach (var step in steps)
        {
            foreach (var (_, reason) in step.Outputs)
            {
                Assert.True(
                    reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"step {name} produced {reason}");
            }
        }
    }
}
