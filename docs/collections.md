---
id: collections
title: Collections
description: Auto-emitted List/array/IEnumerable/IReadOnlyList overloads, nested-element mapping, and the SkipCollectionOverloads opt-out.
sidebar_position: 4
---

# Collections

Real APIs map lists, not single objects. ZeroAlloc.Mapping treats this as the default case: every `[Map<TSrc, TDst>]` declaration emits four collection overloads alongside the single-element method, and every `[TryMap<TSrc, TDst>]` does the same with per-element failure aggregation. You don't write list-returning helpers; the generator does.

## The Four Auto-Emitted Overloads

For every `[Map<TSrc, TDst>]` on a class, the generator emits — in addition to `Map(TSrc) → TDst`:

- `Map(List<TSrc>) → List<TDst>` — capacity-presized `for`-loop, `__dst.Add(Map(src[i]))` per element.
- `Map(TSrc[]) → TDst[]` — `Length`-sized array, indexed write per element.
- `Map(IEnumerable<TSrc>) → IEnumerable<TDst>` — lazy `Enumerable.Select(src, static x => Map(x))`. **No allocation at call time** — the projection is materialised when the consumer enumerates.
- `Map(IReadOnlyList<TSrc>) → IReadOnlyList<TDst>` — array-backed, returned as `IReadOnlyList<TDst>`.

Here's the shape, given `[Map<Src, Dst>]`:

```csharp
public sealed record Src(int X);
public sealed record Dst(int X);

[Map<Src, Dst>]
public static partial class M { }
```

Emits (relevant overloads, formatted from the snapshot):

```csharp
public static List<Dst> Map(List<Src> src)
{
    ArgumentNullException.ThrowIfNull(src);
    var __dst = new List<Dst>(src.Count);
    for (int i = 0; i < src.Count; i++)
        __dst.Add(Map(src[i]));
    return __dst;
}

public static Dst[] Map(Src[] src)
{
    ArgumentNullException.ThrowIfNull(src);
    var __dst = new Dst[src.Length];
    for (int i = 0; i < src.Length; i++)
        __dst[i] = Map(src[i]);
    return __dst;
}

public static IEnumerable<Dst> Map(IEnumerable<Src> src)
{
    ArgumentNullException.ThrowIfNull(src);
    return Enumerable.Select(src, static x => Map(x));
}

public static IReadOnlyList<Dst> Map(IReadOnlyList<Src> src)
{
    ArgumentNullException.ThrowIfNull(src);
    var __dst = new Dst[src.Count];
    for (int i = 0; i < src.Count; i++)
        __dst[i] = Map(src[i]);
    return __dst;
}
```

Two things worth pointing out:

1. The `List<T>` overload **presizes capacity to `src.Count`** before the loop. No re-grow allocations during fill — one backing array, sized once.
2. The `IEnumerable<T>` overload returns a deferred `Select`. Materialisation cost is paid by the consumer's `ToList`/`ToArray`/`foreach`, not at the call site.

## `[TryMap]` Collection Overloads

`[TryMap]` produces fallible mappers that return `Result<TDst, MappingError>`. The collection overloads materialise eagerly and aggregate per-element failures into `MappingError.Children`. The aggregate code is `mapping.collection.elements_failed`, and each child error carries an index-prefixed `PropertyPath` like `[5].Email`.

```csharp
public sealed record SignUpRequest(string Email);
public sealed record User
{
    public User(string email)
    {
        if (string.IsNullOrEmpty(email))
            throw new ArgumentException("Email cannot be empty.");
        Email = email;
    }
    public string Email { get; init; }
}

[TryMap<SignUpRequest, User>]
public static partial class M { }
```

```csharp
var result = M.TryMap(new List<SignUpRequest>
{
    new("ok@example.com"),
    new(""),
    new("ok2@example.com")
});

// result.IsSuccess              == false
// result.Error.Code             == "mapping.collection.elements_failed"
// result.Error.Children![0]     -> PropertyPath "[1]", Code "mapping.constructor.threw"
// result.Error.Reason           == "1 of 3 elements failed"
```

Path concatenation is faithful: if a per-element failure carries a nested path like `Profile.Address.City`, the aggregate child becomes `[1].Profile.Address.City`. `(root)` on the inner error collapses to just `[1]` on the outer.

The lazy `IEnumerable<TSrc>` overload still materialises eagerly under `[TryMap]` — there is no streaming `Result<IEnumerable<...>>` story. The generator allocates a `List<TDst>` internally, drives the projection to completion, and returns the materialised list typed as `IEnumerable<TDst>`.

## Nested Element Mapping

When a destination property is itself a collection of a different type than the source's collection element, the generator wires the per-element mapper from a sibling `[Map<TElem, UElem>]` declaration on the same class.

```csharp
public sealed record OrderItemRequest(int Sku);
public sealed record OrderItem(int Sku);

public sealed record OrderRequest(int Id, List<OrderItemRequest> Items);
public sealed record Order(int Id, List<OrderItem> Items);

[Map<OrderRequest, Order>]
[Map<OrderItemRequest, OrderItem>]
public static partial class M { }
```

The generator finds `[Map<OrderItemRequest, OrderItem>]` on the same class and emits the outer mapping as `Items: Map(src.Items)` — calling its own auto-emitted `List<OrderItemRequest> → List<OrderItem>` overload. No reflection, no DI, no registration step; the wiring is resolved at compile time from the attributes on the class.

This composes the same way for nested object properties (not collections) — declare `[Map<CustomerRequest, Customer>]` alongside the outer mapping and the generator chains the sibling call automatically.

## Opt-Out — `[SkipCollectionOverloads]`

Class-level marker. Suppresses generation of the four collection overloads for **every** `[Map]`/`[TryMap]` on the class. The single-element method still emits.

```csharp
[Map<OrderRequest, Order>]
[SkipCollectionOverloads]
public static partial class M { }
// Only Map(OrderRequest) → Order is emitted.
```

When you'd reach for it:

- **Hot-path mappers** that only ever receive a single element. Cuts four method bodies of dead weight.
- **Smaller assembly size** — relevant for native-AOT footprint or library packages where surface area matters.
- **Surface-area control** — if a public mapper class shouldn't expose collection overloads (e.g. you want callers to drive the loop themselves with custom error handling), the marker keeps the surface tight.

`[SkipCollectionOverloads]` applies symmetrically to `[TryMap]`. The single-element `TryMap(TSrc) → Result<TDst, MappingError>` always emits; the four fallible collection overloads do not.

## Allocation Costs

The success path for collection overloads allocates exactly the destination container — `List<TDst>` (capacity-presized, single backing array), `TDst[]`, or the array surfaced as `IReadOnlyList<TDst>`. The `IEnumerable<TDst>` lazy overload allocates only the `Select` iterator until the consumer materialises it.

For the per-budget numbers and BenchmarkDotNet baselines, see [Performance](performance.md).

## Where to Next

- **Bidirectional mappers** — declare both directions with one attribute via [Reverse Mapping](reverse-mapping.md).
- **Performance baselines** — allocation budgets and BenchmarkDotNet results in [Performance](performance.md).
