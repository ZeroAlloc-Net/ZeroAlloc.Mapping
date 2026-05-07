using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class FlatMapEmissionTests
{
    [Fact]
    public Task FlatMap_TwoIdenticalProperties_Emits_DirectAssignment()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record OrderRequest(int Id, string Notes);
            public sealed record Order(int Id, string Notes);
            [Map<OrderRequest, Order>]
            public static partial class AppMappings { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task FlatMap_With_MapPropertyRename()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Foo);
            public sealed record Dst(string Bar);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Foo", "Bar")]
                public static partial Dst Map(Src src);
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task FlatMap_With_MapValueConstant()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id, string CreatedAt);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapValue("CreatedAt", "2026-05-07T00:00:00Z")]
                public static partial Dst Map(Src src);
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task FlatMap_With_MapperIgnoreTarget()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst
            {
                public Dst(int Id) { Id = Id; }
                public int Id { get; init; }
                [MapperIgnoreTarget]
                public string? Notes { get; init; }
            }
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
