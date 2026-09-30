using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// [TryMap] output compiles when a member needs a nested mapping and when the source is a value
/// type (#140). Both used to emit code that did not compile, with no diagnostic.
/// </summary>
public class TryMapChainTests
{
    private const string Types = """
        #nullable enable
        using ZeroAlloc.Mapping;
        using System.Collections.Generic;
        namespace App
        {
            public sealed record I1(int X);
            public sealed record I2(int X);
            public readonly record struct V1(int X);
            public readonly record struct V2(int X);
        }
        """;

    [Theory]
    [InlineData("public sealed record O1(I1 C); public sealed record O2(I2 C);", "[TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(I1 C); public sealed record O2(I2 C);", "[Map<I1, I2>]")]
    [InlineData("public sealed record O1(I1 C); public sealed record O2(I2 C);", "[Map<I1, I2>][TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(I1? C); public sealed record O2(I2? C);", "[TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(I1? C); public sealed record O2(I2? C);", "[Map<I1, I2>]")]
    [InlineData("public sealed record O1(V1 C); public sealed record O2(V2 C);", "[TryMap<V1, V2>]")]
    [InlineData("public sealed record O1(List<I1> C); public sealed record O2(List<I2> C);", "[TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(List<I1> C); public sealed record O2(I2[] C);", "[TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(I1[] C); public sealed record O2(IReadOnlyList<I2> C);", "[TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(IEnumerable<I1>? C); public sealed record O2(IList<I2>? C);", "[TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(List<I1?> C); public sealed record O2(List<I2?> C);", "[TryMap<I1, I2>]")]
    [InlineData("public sealed record O1(List<I1> C); public sealed record O2(List<I2> C);", "[Map<I1, I2>]")]
    [InlineData("public sealed record O1(List<V1> C); public sealed record O2(V2[] C);", "[TryMap<V1, V2>]")]
    public void NestedMember_UnderTryMap_Compiles(string types, string nested)
    {
        var run = TestHarness.RunGeneratorAndCompile(Types + $$"""
            namespace App
            {
                {{types}}
                [TryMap<O1, O2>]
                {{nested}}
                public static partial class M { }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Errors);
        Assert.DoesNotContain(run.Warnings, d => d.Id.StartsWith("CS86", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[TryMap<V1, I2>]")]
    [InlineData("[TryMap<V1?, App.Presence>]")]
    [InlineData("[TryMap<System.DateTime, App.Ymd>]")]
    public void ValueTypeSource_UnderTryMap_Compiles(string attribute)
    {
        var run = TestHarness.RunGeneratorAndCompile(Types + $$"""
            namespace App
            {
                public sealed record Ymd(int Year, int Month);
                public sealed record Presence(bool HasValue);
                {{attribute}}
                public static partial class M { }
            }
            """);

        Assert.DoesNotContain(run.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(run.Errors);
    }

    /// <summary>
    /// A destination parameter with no source is ZAMP001 under [TryMap] as under [Map]. The
    /// constructor call cannot be built without it, so it used to fail only as CS7036 in the
    /// generated code.
    /// </summary>
    [Fact]
    public void DestinationParameterWithoutSource_UnderTryMap_ReportsZamp001()
    {
        var diagnostics = TestHarness.RunDiagnostics("""
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, int B);
            [TryMap<Src, Dst>]
            public static partial class M { }
            """);

        var zamp001 = Assert.Single(diagnostics, d => d.Id == "ZAMP001");
        Assert.Contains("'B' on 'Dst'", zamp001.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// A parameter with a default value keeps it when nothing maps to it, so it is not ZAMP001
    /// under either attribute.
    /// </summary>
    [Theory]
    [InlineData("Map")]
    [InlineData("TryMap")]
    public void OptionalParameterWithoutSource_IsNotZamp001(string kind)
    {
        var run = TestHarness.RunGeneratorAndCompile(Types + $$"""
            namespace App
            {
                public sealed record Src(int A);
                public sealed record Dst(int A, int B = 7);
                [{{kind}}<Src, Dst>]
                public static partial class M { }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Errors);
    }
}
