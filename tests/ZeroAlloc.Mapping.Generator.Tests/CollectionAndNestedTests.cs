using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class CollectionAndNestedTests
{
    [Fact]
    public Task Collection_ListOfRecords_Emits_PerElementMap()
    {
        var source = """
            using ZeroAlloc.Mapping;
            using System.Collections.Generic;
            public sealed record OrderItemRequest(int Sku);
            public sealed record OrderItem(int Sku);
            public sealed record OrderRequest(int Id, List<OrderItemRequest> Items);
            public sealed record Order(int Id, List<OrderItem> Items);
            [Map<OrderRequest, Order>]
            [Map<OrderItemRequest, OrderItem>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Nested_Object_Chains_To_DeclaredMapper()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record CustomerRequest(string Name);
            public sealed record Customer(string Name);
            public sealed record OrderRequest(int Id, CustomerRequest Customer);
            public sealed record Order(int Id, Customer Customer);
            [Map<OrderRequest, Order>]
            [Map<CustomerRequest, Customer>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
