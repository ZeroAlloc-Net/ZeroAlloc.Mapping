namespace ZeroAlloc.Mapping.Generator.Tests;

public class CycleSafeDiagnosticTests
{
    [Fact]
    public void ZAMP018_FiresWhen_CycleSafe_References_NonCycleSafe_Nested()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record CustomerSrc(string Name);
            public sealed record CustomerDst(string Name);
            public sealed record OrderSrc(int Id, CustomerSrc? Customer);
            public sealed record OrderDst(int Id, CustomerDst? Customer);
            [Map<OrderSrc, OrderDst>(CycleSafe = true)]
            [Map<CustomerSrc, CustomerDst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP018");
    }

    [Fact]
    public void ZAMP018_DoesNotFire_When_Whole_Graph_CycleSafe()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record CustomerSrc(string Name);
            public sealed record CustomerDst(string Name);
            public sealed record OrderSrc(int Id, CustomerSrc? Customer);
            public sealed record OrderDst(int Id, CustomerDst? Customer);
            [Map<OrderSrc, OrderDst>(CycleSafe = true)]
            [Map<CustomerSrc, CustomerDst>(CycleSafe = true)]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP018");
    }
}
