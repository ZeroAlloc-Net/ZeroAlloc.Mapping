using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class ReverseMapTests
{
    [Fact]
    public Task ReverseMap_Emits_Both_Directions()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Order(int Id, string Notes);
            public sealed record OrderDto(int Id, string Notes);
            [ReverseMap<Order, OrderDto>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void ZAMP009_ReverseMap_With_MapProperty_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Foo);
            public sealed record Dst(string Bar);
            [ReverseMap<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Foo", "Bar")]
                public static partial Dst Map(Src src);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP009");
    }
}
