namespace ZeroAlloc.Mapping;

/// <summary>
/// Declares a runtime-polymorphic dispatcher: <c>static TBaseDestination Map(TBase src)</c>
/// that pattern-matches on the source's runtime type and forwards to a derived
/// <c>[Map&lt;TDerived, TDerivedDto&gt;]</c> declared on the same class. Unmatched runtime
/// types throw <c>InvalidOperationException</c>. For a fallible variant use
/// <see cref="PolymorphicTryMapAttribute{TBase, TBaseDestination}"/>.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PolymorphicMapAttribute<TBase, TBaseDestination> : System.Attribute { }
