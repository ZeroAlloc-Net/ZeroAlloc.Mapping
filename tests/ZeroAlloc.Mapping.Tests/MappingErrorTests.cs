namespace ZeroAlloc.Mapping.Tests;

public class MappingErrorTests
{
    [Fact]
    public void MappingError_Equals_StructurallyByCodeAndPath()
    {
        var a = new MappingError("mapping.constructor.threw", "Email");
        var b = new MappingError("mapping.constructor.threw", "Email");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MappingError_Children_DefaultsToNull()
    {
        var err = new MappingError("mapping.parse.failed", "Quantity");
        Assert.Null(err.Children);
    }

    [Fact]
    public void MappingError_Children_HoldsPerElementErrors()
    {
        var children = new MappingError[]
        {
            new("mapping.constructor.threw", "Items[5].Email", "invalid format"),
            new("mapping.parse.failed", "Items[17].Quantity", "not a number")
        };
        var aggregate = new MappingError(
            Code: "mapping.collection.elements_failed",
            PropertyPath: "(root)",
            Reason: "2 elements failed",
            Children: children);
        Assert.Equal(2, aggregate.Children!.Count);
        Assert.Equal("Items[5].Email", aggregate.Children[0].PropertyPath);
    }
}
