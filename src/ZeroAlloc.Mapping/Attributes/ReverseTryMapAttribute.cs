namespace ZeroAlloc.Mapping;

/// <summary>
/// Convenience: declares a symmetric fallible mapping. Generator emits both
/// <c>static Result&lt;TDestination, MappingError&gt; TryMap(TSource)</c> and
/// <c>static Result&lt;TSource, MappingError&gt; TryMap(TDestination)</c>.
/// Equivalent to <c>[TryMap&lt;TSrc, TDst&gt;]</c> + <c>[TryMap&lt;TDst, TSrc&gt;]</c>.
/// Customisations on the partial method are not safely reversible — generator emits ZAMP009.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ReverseTryMapAttribute<TSource, TDestination> : System.Attribute { }
