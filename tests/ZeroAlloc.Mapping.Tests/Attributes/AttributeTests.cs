namespace ZeroAlloc.Mapping.Tests.Attributes;

public class AttributeTests
{
    [Fact]
    public void MapAttribute_AllowsMultiple_OnSameClass()
    {
        var attrs = typeof(SampleMappings).GetCustomAttributes(typeof(MapAttribute<int, string>), inherit: false);
        Assert.Single(attrs);
    }

    [Fact]
    public void TryMapAttribute_DistinctFromMapAttribute()
    {
        Assert.NotEqual(typeof(MapAttribute<int, string>), typeof(TryMapAttribute<int, string>));
    }

    [Map<int, string>]
    [TryMap<long, decimal>]
    private static partial class SampleMappings { }
}
