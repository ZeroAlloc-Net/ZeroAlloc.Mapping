using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class HooksTests
{
    [Fact]
    public Task BeforeMap_Hook_Inlined_BeforeConstructor()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>]
            public static partial class M
            {
                [BeforeMap]
                public static void Validate(Src src) { }
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task AfterMap_Hook_Inlined_AfterAssignment()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>]
            public static partial class M
            {
                [AfterMap]
                public static void Audit(Src src, Dst dst) { }
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Hook_OnMultiMapping_Class_Fires_Only_For_MatchingSourceType()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record A(int X);
            public sealed record B(int X);
            public sealed record P(int Y);
            public sealed record Q(int Y);
            [Map<A, B>]
            [Map<P, Q>]
            public static partial class M
            {
                [BeforeMap]
                public static void OnlyA(A src) { }
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task TryMap_Hooks_Live_Inside_TryBlock()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [TryMap<Src, Dst>]
            public static partial class M
            {
                [BeforeMap]
                public static void Validate(Src src) { }
                [AfterMap]
                public static void Audit(Src src, Dst dst) { }
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
