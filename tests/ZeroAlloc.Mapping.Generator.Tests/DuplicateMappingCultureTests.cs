namespace ZeroAlloc.Mapping.Generator.Tests;

public class DuplicateMappingCultureTests
{
    [Fact]
    public void ZAMP016_DuplicateMappingCulture_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            [MappingCulture("nl-NL")]
            public static partial class M { }
            [MappingCulture("en-US")]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP016");
    }

    [Fact]
    public void ZAMP016_Single_MappingCulture_DoesNotFire()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            [MappingCulture("nl-NL")]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP016");
    }
}
