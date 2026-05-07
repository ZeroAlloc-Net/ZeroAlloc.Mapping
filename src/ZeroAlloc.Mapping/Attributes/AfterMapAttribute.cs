namespace ZeroAlloc.Mapping;

/// <summary>
/// Marks a static method on a <c>[Map]</c>/<c>[TryMap]</c>-decorated class as a hook invoked
/// after each generated mapping body, with both source and destination available. Signature:
/// <c>static void Hook(TSource src, TDestination dst)</c>. Multiple hooks fire in declaration order.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class AfterMapAttribute : System.Attribute { }
