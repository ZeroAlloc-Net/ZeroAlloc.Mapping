namespace ZeroAlloc.Mapping.Generator.Tests;

public class ProjectionDiagnosticTests
{
    [Fact]
    public void ZAMP017_FiresWhen_Projection_With_MappingHook()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>(Projection = true)]
            public static partial class M
            {
                [BeforeMap]
                public static void Before(Src s) { /* unused */ }
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP017");
    }

    [Fact]
    public void ZAMP017_FiresWhen_Projection_With_MappingCulture()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>(Projection = true)]
            [MappingCulture("nl-NL")]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP017");
    }

    [Fact]
    public void ZAMP017_DoesNotFire_When_Projection_Without_IncompatibleFeatures()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id, string Name);
            public sealed record Dst(int Id, string Name);
            [Map<Src, Dst>(Projection = true)]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP017");
    }
}
