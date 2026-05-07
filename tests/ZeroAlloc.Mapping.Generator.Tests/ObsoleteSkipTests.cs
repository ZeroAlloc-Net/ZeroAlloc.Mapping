using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class ObsoleteSkipTests
{
    [Fact]
    public Task Obsolete_SourceProperty_IsSilentlyIgnored()
    {
        var source = """
            using System;
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, [property: Obsolete] int OldField);
            public sealed record Dst(int A);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Obsolete_DestinationParam_TreatedAs_IgnoreTarget()
    {
        var source = """
            using System;
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, [property: Obsolete] string? OldField = null);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
