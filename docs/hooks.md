---
id: hooks
title: Hooks
description: [BeforeMap] and [AfterMap] hooks for validation, logging, and post-mapping computations.
sidebar_position: 8
---

# Hooks

Mapping bodies are single-purpose by design — match properties, run conversions, return the destination. When you need to do something *around* the mapping (validate the source, audit the result, populate a derived field that doesn't fit the property-matching model), `[BeforeMap]` and `[AfterMap]` give you two extension points that the generator inlines directly into the emitted body.

## Pattern

Declare a static method on the same `[Map]`-decorated class and tag it `[BeforeMap]` or `[AfterMap]`:

```csharp
[Map<OrderRequest, Order>]
public static partial class M
{
    [BeforeMap]
    public static void Validate(OrderRequest src)
    {
        if (src.Id <= 0) throw new ArgumentException("Id must be positive");
    }

    [AfterMap]
    public static void Audit(OrderRequest src, Order dst)
    {
        Console.WriteLine($"Mapped order {src.Id}");
    }
}
```

## Signatures

- `[BeforeMap]` — `static void Hook(TSource src)`. Single argument, the source.
- `[AfterMap]` — `static void Hook(TSource src, TDestination dst)`. Source first, destination second.

Both are fire-and-forget — the return value is `void` and there's no way to short-circuit the mapping from a hook. If you need the mapping to fail, throw an exception (and use `[TryMap]` if you want that exception surfaced as a structured `MappingError` rather than rethrown).

## Generated Body

Hooks are inlined verbatim into the emitted method, in declaration order:

```csharp
public static Dst Map(Src src)
{
    ArgumentNullException.ThrowIfNull(src);
    Validate(src);                      // [BeforeMap]
    var __dst = new Dst(
        Id: src.Id
    );
    Audit(src, __dst);                  // [AfterMap]
    return __dst;
}
```

Source fixture: `HooksTests.BeforeMap_Hook_Inlined_BeforeConstructor` and `HooksTests.AfterMap_Hook_Inlined_AfterAssignment`. There's no virtual call, no delegate allocation, and no list-of-hooks dispatch loop — each hook is a direct static call site.

If you declare multiple hooks of the same kind, they all fire in declaration order:

```csharp
[BeforeMap] public static void First(Src src) { }
[BeforeMap] public static void Second(Src src) { }
// emitted: First(src); Second(src); var __dst = new Dst(...);
```

## Variance — Base-Class Hook Parameters

Hook parameter types follow the C# implicit-conversion rules. A hook whose parameter is `SrcBase` fires for any mapping whose source type is `SrcBase` *or any derived type*:

```csharp
public abstract record SrcBase(int Id);
public sealed record SrcDerived(int Id, string Extra) : SrcBase(Id);
public sealed record Dst(int Id);

[Map<SrcDerived, Dst>]
public static partial class M
{
    [BeforeMap]
    public static void OnBase(SrcBase src) { }   // Fires inside Map(SrcDerived → Dst).
}
```

Source fixture: `HooksTests.BeforeMap_Hook_Fires_For_Derived_Source_Type`.

The generator uses the Roslyn `Compilation.ClassifyConversion` API to test assignability — implicit reference conversions, identity, and boxing all qualify. This means you can write a single hook against a base interface and have it fire across every concrete mapping that uses an implementation, which is the right shape for cross-cutting validation.

## Multi-Mapping Source-Type Matching

When a class declares multiple `[Map<,>]` pairs with different source types, a hook only inlines into the body of the mapping whose source type matches (under the variance rule above). It does **not** become a global hook for every mapping on the class:

```csharp
[Map<A, B>]
[Map<P, Q>]
public static partial class M
{
    [BeforeMap]
    public static void OnlyA(A src) { }
    // OnlyA(src) appears inside Map(A → B), but NOT inside Map(P → Q).
}
```

Source fixture: `HooksTests.Hook_OnMultiMapping_Class_Fires_Only_For_MatchingSourceType`.

This is a regression-tested behaviour — earlier versions inlined every hook into every mapping, which produced compile errors when the hook's parameter type didn't match the mapping's source type. The current implementation filters by source-type assignability before inlining.

## `[TryMap]` Interaction — `mapping.hook.threw`

Under `[TryMap]`, hook exceptions are caught and surfaced as structured `MappingError` values rather than propagating to the caller. The generator wraps **each hook in its own try/catch** so the error code can distinguish hook failures from constructor failures:

```csharp
public static Result<Dst, MappingError> TryMap(Src src)
{
    if (src is null) return Result<Dst, MappingError>.Failure(
        new MappingError("mapping.source.null", "(root)"));
    try
    {
        try { Validate(src); }
        catch (Exception hookEx) { return Result<Dst, MappingError>.Failure(
            new MappingError("mapping.hook.threw", "(root)", hookEx.Message)); }
        var __dst = new Dst(
            Id: src.Id
        );
        try { Audit(src, __dst); }
        catch (Exception hookEx) { return Result<Dst, MappingError>.Failure(
            new MappingError("mapping.hook.threw", "(root)", hookEx.Message)); }
        return Result<Dst, MappingError>.Success(__dst);
    }
    catch (Exception ex)
    {
        return Result<Dst, MappingError>.Failure(
            new MappingError("mapping.constructor.threw", "(root)", ex.Message));
    }
}
```

Source fixture: `HooksTests.TryMap_Hooks_Live_Inside_TryBlock`.

The error-code split lets callers distinguish "the source data was bad" (`mapping.constructor.threw` — likely something like `int.Parse` rejecting an invalid string) from "a validation hook rejected the input" (`mapping.hook.threw` — your own code chose to fail). Pattern-match on `result.Error.Code` to route appropriately:

```csharp
var result = M.TryMap(new Src(-1));
switch (result.Error?.Code)
{
    case "mapping.hook.threw":         // hook validation failure
    case "mapping.constructor.threw":  // conversion or constructor failure
    case "mapping.source.null":        // null guard
}
```

Under `[Map]` (non-fallible), hook exceptions throw uncaught — same contract as the rest of the constructor form. There's no `try/catch` wrapping the body, so anything a hook throws bubbles to the caller as the exception type the hook raised.

## Update-in-Place

Hooks fire normally for the void overload — `[BeforeMap]` runs before the assignments, `[AfterMap]` runs after with `existingDst` as the second argument:

```csharp
public static partial void Map(Src src, Dst existingDst)
{
    ArgumentNullException.ThrowIfNull(src);
    ArgumentNullException.ThrowIfNull(existingDst);
    Validate(src);
    existingDst.Id = src.Id;
    Audit(src, existingDst);
}
```

See [Update-in-Place](update-in-place.md) for the full void-overload contract.

## Where to Next

- **Polymorphic dispatch** — runtime-type switching across an inheritance hierarchy. See [Polymorphic Dispatch](polymorphic.md).
- **Diagnostics** — every error code the generator can emit, including the `mapping.*` runtime codes used by `[TryMap]`. See [Diagnostics](diagnostics.md).
