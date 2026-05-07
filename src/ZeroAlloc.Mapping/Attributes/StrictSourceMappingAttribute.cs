namespace ZeroAlloc.Mapping;

/// <summary>
/// Marker — when applied to a <c>[Map]</c>/<c>[TryMap]</c>-decorated <c>static partial class</c>,
/// requires every source property to be either consumed by a destination parameter or marked
/// <c>[MapperIgnoreSource]</c>. Unconsumed sources fire ZAMP010 (Error). Default is permissive.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StrictSourceMappingAttribute : System.Attribute { }
