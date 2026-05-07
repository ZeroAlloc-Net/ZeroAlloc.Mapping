namespace ZeroAlloc.Mapping;

/// <summary>
/// Declares an infallible mapping from <typeparamref name="TSource"/> to <typeparamref name="TDestination"/>.
/// The generator emits a <c>static TDestination Map(TSource src)</c> method into the decorated
/// <c>static partial class</c>. Inner failures (smart-constructor exceptions, <c>Parse</c> exceptions,
/// nested-mapper exceptions) propagate uncaught. For a non-throwing path use
/// <see cref="TryMapAttribute{TSource, TDestination}"/>.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapAttribute<TSource, TDestination> : System.Attribute { }
