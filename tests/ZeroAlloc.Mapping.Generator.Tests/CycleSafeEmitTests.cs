using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class CycleSafeEmitTests
{
    [Fact]
    public Task SelfRef()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace MyApp;
            public sealed class Node { public string Name { get; set; } = ""; public Node? Self { get; set; } }
            public sealed class NodeDst { public string Name { get; set; } = ""; public NodeDst? Self { get; set; } }
            [Map<Node, NodeDst>(CycleSafe = true)]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task MutualRef()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace MyApp;
            public sealed class CustomerSrc { public string Name { get; set; } = ""; public OrderSrc? LastOrder { get; set; } }
            public sealed class OrderSrc { public int Id { get; set; } public CustomerSrc? Customer { get; set; } }
            public sealed class CustomerDst { public string Name { get; set; } = ""; public OrderDst? LastOrder { get; set; } }
            public sealed class OrderDst { public int Id { get; set; } public CustomerDst? Customer { get; set; } }
            [Map<OrderSrc, OrderDst>(CycleSafe = true)]
            [Map<CustomerSrc, CustomerDst>(CycleSafe = true)]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task CollectionOfNested()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace MyApp;
            public sealed class ItemSrc { public string Name { get; set; } = ""; public ParentSrc? Parent { get; set; } }
            public sealed class ItemDst { public string Name { get; set; } = ""; public ParentDst? Parent { get; set; } }
            public sealed class ParentSrc { public int Id { get; set; } public System.Collections.Generic.List<ItemSrc>? Items { get; set; } }
            public sealed class ParentDst { public int Id { get; set; } public System.Collections.Generic.List<ItemDst>? Items { get; set; } }
            [Map<ParentSrc, ParentDst>(CycleSafe = true)]
            [Map<ItemSrc, ItemDst>(CycleSafe = true)]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
