namespace ZeroAlloc.Mapping;

/// <summary>
/// Marks a static method on a <c>[Map]</c>/<c>[TryMap]</c>-decorated class as a hook invoked
/// before each generated mapping body. Method signature:
/// <c>static void Hook(TSource src)</c>. Multiple <c>[BeforeMap]</c> hooks may be declared;
/// they fire in declaration order.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class BeforeMapAttribute : System.Attribute { }
