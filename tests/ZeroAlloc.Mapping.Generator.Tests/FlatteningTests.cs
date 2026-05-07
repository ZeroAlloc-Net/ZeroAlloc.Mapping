using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class FlatteningTests
{
    [Fact]
    public Task Flatten_TwoLevels_Emits_NullForgivingPath_Under_Map()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Address(string City);
            public sealed record Customer(Address Address);
            public sealed record Src(Customer Customer);
            public sealed record Dst(string City);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Customer.Address.City", "City")]
                public static partial Dst Map(Src src);
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Flatten_NullableSource_EmitsNullConditional()
    {
        var source = """
            #nullable enable
            using ZeroAlloc.Mapping;
            public sealed record Address(string City);
            public sealed record Customer(Address? Address);
            public sealed record Src(Customer? Customer);
            public sealed record Dst(string? City);
            [Map<Src?, Dst?>]
            public static partial class M
            {
                [MapProperty("Customer.Address.City", "City")]
                public static partial Dst? Map(Src? src);
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void ZAMP005_MissingDottedSegment_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Address(string City);
            public sealed record Customer(Address Address);
            public sealed record Src(Customer Customer);
            public sealed record Dst(string City);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Customer.NoSuchProp.City", "City")]
                public static partial Dst Map(Src src);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP005");
    }
}
