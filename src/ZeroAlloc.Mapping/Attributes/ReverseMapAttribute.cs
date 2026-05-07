namespace ZeroAlloc.Mapping;

/// <summary>
/// Convenience: declares a symmetric mapping. Generator emits both
/// <c>static TDestination Map(TSource)</c> and <c>static TSource Map(TDestination)</c>.
/// Equivalent to <c>[Map&lt;TSrc, TDst&gt;]</c> + <c>[Map&lt;TDst, TSrc&gt;]</c>.
/// Customisations on the partial method (<c>[MapProperty]</c>, <c>[MapValue]</c>,
/// <c>[MapperIgnoreTarget]</c>) are not safely reversible — generator emits ZAMP009.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ReverseMapAttribute<TSource, TDestination> : System.Attribute { }
