using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class ProjectionEmitTests
{
    [Fact]
    public Task Flat()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace MyApp;
            public sealed record Src(int Id, string Name);
            public sealed record Dst(int Id, string Name);
            [Map<Src, Dst>(Projection = true)]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task NestedInlined()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace MyApp;
            public sealed record CustomerSrc(string Name, int Age);
            public sealed record CustomerDst(string Name, int Age);
            public sealed record OrderSrc(int Id, CustomerSrc Customer);
            public sealed record OrderDst(int Id, CustomerDst Customer);
            [Map<OrderSrc, OrderDst>(Projection = true)]
            [Map<CustomerSrc, CustomerDst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
