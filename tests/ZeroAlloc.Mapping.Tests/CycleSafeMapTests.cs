namespace ZeroAlloc.Mapping.Tests;

using TestFixtures;

public class CycleSafeMapTests
{
    [Fact]
    public void SelfCycle_ResolvesToSameInstance()
    {
        var src = new NodeSrc { Name = "root" };
        src.Self = src;   // self-cycle

        var dst = NodeMappings.Map(src);

        Assert.Equal("root", dst.Name);
        Assert.Same(dst, dst.Self);    // The cycle resolves: dst's Self is dst itself.
    }

    [Fact]
    public void MutualBackRef_ResolvesViaTracker()
    {
        var customer = new CustomerSrc { Name = "alice" };
        var order = new OrderSrc { Id = 7, Customer = customer };
        customer.LastOrder = order;    // mutual back-ref

        var dstOrder = OrderMappings.Map(order);

        Assert.Equal(7, dstOrder.Id);
        Assert.NotNull(dstOrder.Customer);
        Assert.Equal("alice", dstOrder.Customer.Name);
        Assert.NotNull(dstOrder.Customer.LastOrder);
        Assert.Same(dstOrder, dstOrder.Customer.LastOrder);   // The cycle resolves.
    }
}
