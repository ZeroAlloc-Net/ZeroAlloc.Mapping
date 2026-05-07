using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class DiscoveryTests
{
    [Fact]
    public Task EmitsStub_For_Map_Decorated_Class()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record A(int X);
            public sealed record B(int X);
            [Map<A, B>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void EmitsNothing_For_Compilation_Without_MapAttribute()
    {
        var source = """
            public sealed record A(int X);
            public sealed record B(int X);
            public static partial class M { }
            """;
        var output = TestHarness.RunGenerator(source);
        Assert.Equal("", output);
    }
}
