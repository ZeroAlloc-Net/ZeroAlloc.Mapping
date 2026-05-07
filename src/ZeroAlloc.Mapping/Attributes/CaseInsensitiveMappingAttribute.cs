namespace ZeroAlloc.Mapping;

/// <summary>
/// Marker — when applied to a <c>[Map]</c>/<c>[TryMap]</c>-decorated <c>static partial class</c>,
/// switches property-name matching to case-insensitive across every declared mapping on that class.
/// Default is case-sensitive.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CaseInsensitiveMappingAttribute : System.Attribute { }
