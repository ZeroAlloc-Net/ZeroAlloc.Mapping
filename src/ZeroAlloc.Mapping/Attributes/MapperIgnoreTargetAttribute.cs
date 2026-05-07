namespace ZeroAlloc.Mapping;

/// <summary>
/// Marks a destination property as intentionally unmapped — the generator skips it during emission
/// instead of failing with ZAMP001 ("destination property has no source").
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MapperIgnoreTargetAttribute : System.Attribute { }
