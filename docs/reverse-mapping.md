---
id: reverse-mapping
title: Reverse Mapping
description: Bidirectional mappers via [ReverseMap<,>] — symmetric DTO ↔ domain in one declaration.
sidebar_position: 5
---

# Reverse Mapping

Most CRUD endpoints need the same mapping in both directions: domain → DTO on the read path, DTO → domain on the write path. `[ReverseMap<A, B>]` declares both at once.

## Desugaring

`[ReverseMap<A, B>]` is exactly equivalent to two `[Map]` declarations on the same class:

```text
[ReverseMap<A, B>]   ≡   [Map<A, B>] + [Map<B, A>]
[ReverseTryMap<A, B>] ≡  [TryMap<A, B>] + [TryMap<B, A>]
```

No magic, no shared body — the generator splits the attribute into two forward `[Map]` declarations and runs the rest of the pipeline. The two emitted methods are independent overloads sharing nothing but the partial class they live on.

```csharp
public sealed record Order(int Id, string Notes);
public sealed record OrderDto(int Id, string Notes);

[ReverseMap<Order, OrderDto>]
public static partial class M { }

// Both directions are now callable:
var dto = M.Map(new Order(1, "n"));        // Order → OrderDto
var domain = M.Map(new OrderDto(1, "n"));  // OrderDto → Order
```

Each direction also gets its own four collection overloads (see [Collections](collections.md)) — `Map(List<Order>) → List<OrderDto>` and `Map(List<OrderDto>) → List<Order>`, etc.

## When to Use It

`[ReverseMap]` is for **symmetric** pairs — types where the forward and reverse shapes match by name and type without per-direction tweaks. Read/write DTOs that mirror their domain counterpart, persistence records that round-trip through a database layer, integration shims that translate between two equivalent representations.

If you find yourself wanting to rename a property in one direction but not the other, or inject a constant on one side, the pair isn't symmetric — switch to two explicit `[Map]` declarations.

## Limitation — ZAMP009

Customisations that change *which* properties pair with *what* aren't safely reversible: `[MapProperty]` rename, `[MapValue]` constant injection, and `[MapperIgnoreTarget]` exclusion all carry information about one direction that isn't valid in the other. The generator detects this and emits `ZAMP009` (Error) telling you to write two explicit forward declarations.

```csharp
public sealed record Src(string Foo);
public sealed record Dst(string Bar);

[ReverseMap<Src, Dst>]
public static partial class M
{
    [MapProperty("Foo", "Bar")]
    public static partial Dst Map(Src src);
}
// ZAMP009: customisations on a [ReverseMap] partial are not safely reversible.
```

The fix is to split the attribute and customise only the direction that needs it:

```csharp
public sealed record Src(string Foo);
public sealed record Dst(string Bar);

[Map<Src, Dst>]
[Map<Dst, Src>]
public static partial class M
{
    [MapProperty("Foo", "Bar")]
    public static partial Dst Map(Src src);

    [MapProperty("Bar", "Foo")]
    public static partial Src Map(Dst src);
}
```

`[ReverseMap]` exists as the safe shorthand for the no-customisation case; `ZAMP009` is the guard that keeps it from silently emitting an asymmetric pair.

## Per-Direction Customisation

You can declare a partial method for one direction only — the generator implements that direction's customisations and lets the other use the default by-name match. The catch: if the partial carries `[MapProperty]`, `[MapValue]`, or `[MapperIgnoreTarget]`, `ZAMP009` still fires because those attributes still aren't reversible.

```csharp
[ReverseMap<Order, OrderDto>]
public static partial class M
{
    // Forward direction declared; reverse uses default by-name match.
    public static partial OrderDto Map(Order src);
    // ZAMP009 if [MapProperty] / [MapValue] / [MapperIgnoreTarget] is on this partial.
}
```

In practice, if you need any per-direction tweak beyond the default, switch to two explicit `[Map<,>]` declarations. The shorthand only pays its way when both directions are fully symmetric.

## `[ReverseTryMap]`

Same shape, fallible. Both directions return `Result<T, MappingError>` and integrate with the per-element collection aggregation described in [Collections](collections.md).

```csharp
public sealed record Order(int Id, string Notes);
public sealed record OrderDto(int Id, string Notes);

[ReverseTryMap<Order, OrderDto>]
public static partial class M { }

// Both directions emit:
//   static Result<OrderDto, MappingError> TryMap(Order)
//   static Result<Order, MappingError> TryMap(OrderDto)
// Plus the four fallible collection overloads on each direction.
```

The same `ZAMP009` guard applies — customisations on a `[ReverseTryMap]` partial split into two explicit `[TryMap<,>]` declarations.

## Where to Next

- **Polymorphic dispatch** — base/derived hierarchies with a single mapping entry point. See [Polymorphic Dispatch](polymorphic.md).
- **Update-in-place** — apply a source onto an existing destination instance instead of allocating. See [Update-in-Place](update-in-place.md).
