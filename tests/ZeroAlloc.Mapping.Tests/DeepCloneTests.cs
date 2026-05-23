namespace ZeroAlloc.Mapping.Tests;

using TestFixtures;

public class DeepCloneTests
{
    [Fact]
    public void DeepClone_DestinationCollection_IsIndependentOfSource()
    {
        var src = new DcSrc
        {
            OrderId = 7,
            Tags = new System.Collections.Generic.List<Tag>
            {
                new() { Name = "a", Id = 1 },
                new() { Name = "b", Id = 2 },
            },
        };
        var dst = DcMappings.Map(src);

        Assert.NotSame(src.Tags, dst.Tags);
        Assert.Equal(2, dst.Tags!.Count);

        // Mutating source does NOT affect destination.
        src.Tags.Add(new() { Name = "c", Id = 3 });
        Assert.Equal(2, dst.Tags.Count);
    }

    [Fact]
    public void DeepClone_NestedObject_IsIndependentOfSource()
    {
        var src = new DcOrderSrc
        {
            Id = 1,
            Customer = new DcCustomer { Name = "alice", Age = 30 },
        };
        var dst = DcOrderMappings.Map(src);

        Assert.NotSame(src.Customer, dst.Customer);
        Assert.Equal("alice", dst.Customer!.Name);

        // Mutating source's Customer does NOT affect destination.
        src.Customer.Name = "bob";
        Assert.Equal("alice", dst.Customer.Name);
    }
}
