namespace ZeroAlloc.Mapping.Generator.Tests;

public class StrictSourceTests
{
    [Fact]
    public void ZAMP010_UnconsumedSourceProperty_Reported_UnderStrictMode()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B, int C);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            [StrictSourceMapping]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP010");
    }

    [Fact]
    public void Strict_DoesNotFire_WithoutMarker()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B, int C);
            public sealed record Dst(int A);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP010");
    }

    [Fact]
    public void Strict_Honours_MapperIgnoreSource_Suppression()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B, [property: MapperIgnoreSource] int C);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            [StrictSourceMapping]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP010");
    }
}
