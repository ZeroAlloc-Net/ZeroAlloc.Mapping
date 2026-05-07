namespace ZeroAlloc.Mapping.Generator.Tests;

public class DiagnosticTests
{
    [Fact]
    public void ZAMP001_DestinationHasNoSource_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP001");
    }

    [Fact]
    public void ZAMP002_NoConversionPath_Reported()
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
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP002");
    }

    [Fact]
    public void ZAMP003_AmbiguousSource_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X, int Other);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Other", "X")]
                public static partial Dst Map(Src src);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP003");
    }

    [Fact]
    public void ZAMP004_MapChainsTryMap_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Inner1(int X);
            public sealed record Inner2(int X);
            public sealed record Outer1(Inner1 Child);
            public sealed record Outer2(Inner2 Child);
            [Map<Outer1, Outer2>]
            [TryMap<Inner1, Inner2>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP004");
    }

    [Fact]
    public void ZAMP005_MapPropertyMissing_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("DoesNotExist", "X")]
                public static partial Dst Map(Src src);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP005");
    }

    [Fact]
    public void ZAMP006_NotStaticPartialClass_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP006");
    }

    [Fact]
    public void ZAMP007_NullableMismatch_Reported()
    {
        var source = """
            #nullable enable
            using ZeroAlloc.Mapping;
            public sealed record Src(string? Name);
            public sealed record Dst(string Name);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP007");
    }

    [Fact]
    public void ZAMP008_AmbiguousConstructor_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed class Dst
            {
                public Dst(int X, string Y) { }
                public Dst(int X, int Y) { }
            }
            public sealed record Src(int X, string Y);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP008");
    }

    [Fact]
    public void Clean_Source_Emits_No_Diagnostics()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record OrderRequest(int Id, string Notes);
            public sealed record Order(int Id, string Notes);
            [Map<OrderRequest, Order>]
            public static partial class AppMappings { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Empty(diags);
    }
}
