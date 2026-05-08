using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class MappingCultureTests
{
    [Fact]
    public Task MappingCulture_NL_Substitutes_GetCultureInfo_In_ParseCalls()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            [MappingCulture("nl-NL")]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task MappingCulture_Absent_Keeps_InvariantCulture_Default()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
