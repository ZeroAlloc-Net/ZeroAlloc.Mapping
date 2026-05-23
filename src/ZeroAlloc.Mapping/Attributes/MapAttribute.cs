namespace ZeroAlloc.Mapping;

/// <summary>
/// Declares an infallible mapping from <typeparamref name="TSource"/> to <typeparamref name="TDestination"/>.
/// The generator emits a <c>static TDestination Map(TSource src)</c> method into the decorated
/// <c>static partial class</c>. Inner failures (smart-constructor exceptions, <c>Parse</c> exceptions,
/// nested-mapper exceptions) propagate uncaught. For a non-throwing path use
/// <see cref="TryMapAttribute{TSource, TDestination}"/>.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapAttribute<TSource, TDestination> : System.Attribute
{
    /// <summary>
    /// When <c>true</c>, the generator additionally emits a
    /// <c>public static System.Linq.Expressions.Expression&lt;System.Func&lt;TSource, TDestination&gt;&gt; Projection</c>
    /// property on the declaring class for use with EF Core's <c>IQueryable&lt;T&gt;.Select(...)</c>.
    /// Nested <c>[Map&lt;,&gt;]</c> declarations are auto-inlined into the expression tree (EF Core can't
    /// translate cross-mapper calls). Incompatible with <c>[MappingHook]</c>, <c>[MappingCulture]</c>,
    /// and <c>[PolymorphicMap]</c> — see <c>ZAMP017</c>.
    /// Default: <c>false</c>.
    /// </summary>
    public bool Projection { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, the generator rewrites the standard <c>Map(src)</c> body into a tracker-allocating
    /// entry point that forwards to an <c>internal static TDestination Map(TSource src, IDictionary&lt;object, object&gt; tracker)</c>
    /// recursive helper. The tracker (keyed by reference equality) caches already-mapped destinations,
    /// breaking cycles via the cache. Every nested <c>[Map&lt;,&gt;]</c> reachable from a <c>CycleSafe = true</c>
    /// mapping must also declare <c>CycleSafe = true</c> — see <c>ZAMP018</c>.
    /// Default: <c>false</c>.
    /// </summary>
    public bool CycleSafe { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, the generator walks the reachable-type graph at compile time and emits
    /// per-property literal clone code for every reference-typed property of <typeparamref name="TSource"/>
    /// where NO explicit nested <c>[Map&lt;,&gt;]</c> declaration exists. Explicit nested
    /// <c>[Map&lt;,&gt;]</c> declarations always win — they remain the family idiom.
    /// Uncloneable reachable types fire <c>ZAMP019</c>. Cyclic type graphs without <c>CycleSafe = true</c>
    /// fire <c>ZAMP020</c>.
    /// Default: <c>false</c>.
    /// </summary>
    public bool DeepClone { get; init; } = false;
}
