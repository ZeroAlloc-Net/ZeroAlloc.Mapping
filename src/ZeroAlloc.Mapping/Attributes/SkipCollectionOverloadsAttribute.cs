namespace ZeroAlloc.Mapping;

/// <summary>
/// Marker — when applied to a <c>[Map]</c>/<c>[TryMap]</c>-decorated <c>static partial class</c>,
/// suppresses generation of the auto-emitted collection overloads
/// (<c>List&lt;T&gt;</c>, <c>T[]</c>, <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>).
/// Default behaviour (without this marker) emits all four overloads in addition to the
/// single-element form.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SkipCollectionOverloadsAttribute : System.Attribute { }
