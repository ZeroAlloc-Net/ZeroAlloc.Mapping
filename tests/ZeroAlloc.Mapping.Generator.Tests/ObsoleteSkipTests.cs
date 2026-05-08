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

    [Fact]
    public Task Obsolete_Source_With_Explicit_MapPropertyRename_IsHonored()
    {
        var source = """
            using System;
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, [property: Obsolete] int Legacy);
            public sealed record Dst(int A, int Modern);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Legacy", "Modern")]
                public static partial Dst Map(Src src);
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void Obsolete_DestinationParam_DoesNotFire_ZAMP001()
    {
        var source = """
            using System;
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, [property: Obsolete] string? OldField = null);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP001");
    }
}
