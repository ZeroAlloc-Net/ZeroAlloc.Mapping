namespace ZeroAlloc.Mapping;

/// <summary>
/// Marks a source property as intentionally unmapped — suppresses ZAMP00? "unused source property"
/// diagnostics for this property.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MapperIgnoreSourceAttribute : System.Attribute { }
