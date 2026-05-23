using System.Linq;

namespace ZeroAlloc.Mapping.Tests;

public class ProjectionTests
{
    [Fact]
    public void Projection_AppliesToInMemoryQueryable()
    {
        var src = new System.Collections.Generic.List<TestFixtures.ProjSrc>
        {
            new(1, "alpha"),
            new(2, "beta"),
        }.AsQueryable();

        var dst = src.Select(TestFixtures.ProjMappings.Projection).ToList();

        Assert.Collection(dst,
            d => { Assert.Equal(1, d.Id); Assert.Equal("alpha", d.Name); },
            d => { Assert.Equal(2, d.Id); Assert.Equal("beta", d.Name); });
    }
}
