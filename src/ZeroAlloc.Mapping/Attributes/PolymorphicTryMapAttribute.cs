namespace ZeroAlloc.Mapping;

/// <summary>
/// Fallible polymorphic dispatcher: <c>static Result&lt;TBaseDestination, MappingError&gt;
/// TryMap(TBase src)</c>. Unmatched runtime types surface as
/// <c>MappingError("mapping.polymorphic.unhandled_type", "(root)")</c>.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PolymorphicTryMapAttribute<TBase, TBaseDestination> : System.Attribute { }
