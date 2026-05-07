using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class CaseInsensitiveTests
{
    [Fact]
    public Task CaseInsensitive_Matches_DifferentCasing()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string fooBar);
            public sealed record Dst(string FooBar);
            [Map<Src, Dst>]
            [CaseInsensitiveMapping]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void ZAMP011_AmbiguousCaseInsensitiveMatch_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Foo, string foo);
            public sealed record Dst(string FOO);
            [Map<Src, Dst>]
            [CaseInsensitiveMapping]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP011");
    }
}
