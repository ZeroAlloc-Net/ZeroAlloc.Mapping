namespace ZeroAlloc.Mapping;

/// <summary>
/// Declares a fallible mapping from <typeparamref name="TSource"/> to <typeparamref name="TDestination"/>.
/// The generator emits a <c>static Result&lt;TDestination, MappingError&gt; TryMap(TSource src)</c>
/// method into the decorated <c>static partial class</c>. Inner failures (smart-constructor exceptions,
/// <c>Parse</c> exceptions, null sources, nested-mapper failures, collection-element failures) are
/// caught and surfaced as a structured <see cref="MappingError"/>.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TryMapAttribute<TSource, TDestination> : System.Attribute { }
