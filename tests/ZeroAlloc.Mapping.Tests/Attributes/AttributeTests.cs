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

    [Fact]
    public void MapPropertyAttribute_RoundTripsRenameMetadata()
    {
        var attr = new MapPropertyAttribute("Foo", "Bar");
        Assert.Equal("Foo", attr.SourceProperty);
        Assert.Equal("Bar", attr.TargetProperty);
        Assert.Equal(System.AttributeTargets.Method,
            ((System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
                typeof(MapPropertyAttribute), typeof(System.AttributeUsageAttribute))!).ValidOn);
    }

    [Fact]
    public void MapValueAttribute_StoresConstantValue()
    {
        var attr = new MapValueAttribute("CreatedAt", "2026-05-07T00:00:00Z");
        Assert.Equal("CreatedAt", attr.TargetProperty);
        Assert.Equal("2026-05-07T00:00:00Z", attr.Value);
    }

    [Fact]
    public void MapperIgnoreSourceAttribute_TargetsProperty_NotMultiple()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(MapperIgnoreSourceAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void MapperIgnoreTargetAttribute_TargetsProperty_NotMultiple()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(MapperIgnoreTargetAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void CaseInsensitiveMappingAttribute_TargetsClass_NotMultiple()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(CaseInsensitiveMappingAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void StrictSourceMappingAttribute_TargetsClass_NotMultiple()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(StrictSourceMappingAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void BeforeMapAttribute_TargetsMethod_AllowsMultiple()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(BeforeMapAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Method, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    [Fact]
    public void AfterMapAttribute_TargetsMethod_AllowsMultiple()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(AfterMapAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Method, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    [Fact]
    public void ReverseMapAttribute_TargetsClass_AllowsMultiple()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(ReverseMapAttribute<int, string>), typeof(System.AttributeUsageAttribute))!;
        Assert.Equal(System.AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    [Map<int, string>]
    [TryMap<long, decimal>]
    private static partial class SampleMappings { }
}
