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
}
