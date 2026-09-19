
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class MappingCultureTests
{
    [Fact]
    public void MappingCulture_NL_Substitutes_GetCultureInfo_In_ParseCalls()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            [MappingCulture("nl-NL")]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void MappingCulture_Absent_Keeps_InvariantCulture_Default()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }
}
