
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class UpdateInPlaceTests
{
    [Fact]
    public void UpdateInPlace_Settable_Properties_Emits_Assignments()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record OrderRequest(int Id, string Notes);
            public sealed class Order
            {
                public int Id { get; set; }
                public string Notes { get; set; } = "";
            }
            [Map<OrderRequest, Order>]
            public static partial class M
            {
                public static partial void Map(OrderRequest src, Order existingDst);
            }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void UpdateInPlace_Honours_BeforeAfter_Hooks()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed class Dst { public int Id { get; set; } }
            [Map<Src, Dst>]
            public static partial class M
            {
                public static partial void Map(Src src, Dst existingDst);
                [BeforeMap] public static void Validate(Src src) { }
                [AfterMap] public static void Audit(Src src, Dst dst) { }
            }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void UpdateInPlace_Coexists_With_Constructor_Form()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed class Dst { public int Id { get; set; } }
            [Map<Src, Dst>]
            public static partial class M
            {
                public static partial Dst Map(Src src);
                public static partial void Map(Src src, Dst existingDst);
            }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void UpdateInPlace_With_DottedFlattening_Emits_NullForgivingPath()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Address(string City);
            public sealed record Customer(Address Address);
            public sealed record Src(Customer Customer);
            public sealed class Dst { public string City { get; set; } = ""; }
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Customer.Address.City", "City")]
                public static partial void Map(Src src, Dst existingDst);
            }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void ZAMP012_Mixed_Settable_And_InitOnly_POCO_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B);
            public sealed class Dst
            {
                public int A { get; set; }
                public int B { get; init; }
            }
            [Map<Src, Dst>]
            public static partial class M
            {
                public static partial void Map(Src src, Dst existingDst);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP012");
    }

    [Fact]
    public void ZAMP012_InitOnly_Destination_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>]
            public static partial class M
            {
                public static partial void Map(Src src, Dst existingDst);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP012");
    }
}
