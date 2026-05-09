---
id: update-in-place
title: Update-in-Place
description: Mutate an existing destination instance via static partial void Map(TSrc, TDst) — useful for entity tracking and pooling.
sidebar_position: 7
---

# Update-in-Place

The default `Map(TSrc) → TDst` shape constructs a fresh destination on every call. Sometimes that's the wrong contract — you already hold a destination instance and you want to mutate it rather than allocate. The canonical case is **EF Core change tracking**: the framework loads an entity, you receive a request DTO, and you need to apply the DTO's changes to the tracked entity so EF detects the modifications and includes them in `SaveChanges`. Constructing a fresh instance would detach the original and lose the tracking. ZeroAlloc.Mapping's update-in-place overload covers this case.

## Pattern

Declare a `static partial void Map(TSrc, TDst)` overload alongside the `[Map<,>]` decoration:

```csharp
public sealed record OrderRequest(int Id, string Notes);

public sealed class Order   // class, not record — needs settable properties
{
    public int Id { get; set; }
    public string Notes { get; set; } = "";
}

[Map<OrderRequest, Order>]
public static partial class M
{
    public static partial void Map(OrderRequest src, Order existingDst);
}
```

Source fixture: `UpdateInPlaceTests.UpdateInPlace_Settable_Properties_Emits_Assignments`.

The overload's signature is what tells the generator to emit the void-return body. The attribute itself is unchanged — `[Map<OrderRequest, Order>]` is the same one you'd use for the constructor form.

Usage:

```csharp
var existing = await db.Orders.FindAsync(orderId);
M.Map(updateRequest, existing);          // mutate in place
await db.SaveChangesAsync();             // EF picks up the changes
```

## Generated Body

The emitted body is a sequence of property assignments — no constructor invocation:

```csharp
public static partial void Map(OrderRequest src, Order existingDst)
{
    ArgumentNullException.ThrowIfNull(src);
    ArgumentNullException.ThrowIfNull(existingDst);
    existingDst.Id = src.Id;
    existingDst.Notes = src.Notes;
}
```

Both arguments are null-guarded. Properties are assigned in declaration order. The same conversion paths used by the constructor form (numeric widening, nullable→non-nullable, `Parse(string)`, nested `Map`) apply on the right-hand side of each assignment.

## Coexistence with Constructor Form

The constructor `partial Dst Map(Src)` and the void `partial Map(Src, Dst)` can both live on the same class. The generator emits both:

```csharp
[Map<Src, Dst>]
public static partial class M
{
    public static partial Dst Map(Src src);                       // construct fresh
    public static partial void Map(Src src, Dst existingDst);     // update existing
}
```

Source fixture: `UpdateInPlaceTests.UpdateInPlace_Coexists_With_Constructor_Form`.

The constructor form is selected when the caller writes `M.Map(src)`; the void form when they write `M.Map(src, existingDst)`. Standard C# overload resolution — no extra wiring.

## Customisation Flows Through

`[MapProperty]` rename, `[MapValue]` constant injection, `[MapperIgnoreTarget]` skip, and dotted-path flattening all work on the void overload exactly the way they do on the constructor form. Hang the attribute off the void partial:

```csharp
public sealed record Address(string City);
public sealed record Customer(Address Address);
public sealed record Src(Customer Customer);
public sealed class Dst { public string City { get; set; } = ""; }

[Map<Src, Dst>]
public static partial class M
{
    [MapProperty("Customer.Address.City", "City")]
    public static partial void Map(Src src, Dst existingDst);
}
```

The dotted-path null-handling rules from [Flattening](flattening.md) apply unchanged — null on any segment becomes `null!` (or default) on the assignment side.

## Hooks

`[BeforeMap]` runs before the assignments; `[AfterMap]` runs after, with `existingDst` as the second argument:

```csharp
public static partial void Map(Src src, Dst existingDst)
{
    ArgumentNullException.ThrowIfNull(src);
    ArgumentNullException.ThrowIfNull(existingDst);
    Validate(src);                  // [BeforeMap]
    existingDst.Id = src.Id;
    Audit(src, existingDst);        // [AfterMap]
}
```

Source fixture: `UpdateInPlaceTests.UpdateInPlace_Honours_BeforeAfter_Hooks`. See [Hooks](hooks.md) for the full hook contract.

## ZAMP012 — Settable Properties Required

Every destination property the generator needs to assign to must be **public and settable** — not init-only, not read-only. Records with positional parameters compile to init-only properties under the hood, which means a record destination won't work for the void overload.

```csharp
public sealed record Src(int Id);
public sealed record Dst(int Id);   // record positional → init-only setters

[Map<Src, Dst>]
public static partial class M
{
    public static partial void Map(Src src, Dst existingDst);
    // ZAMP012: Id has no public setter — cannot be updated in place.
}
```

Mixed POCOs trip the same diagnostic — if even one matched property is `{ get; init; }` instead of `{ get; set; }`, ZAMP012 fires for that property:

```csharp
public sealed class Dst
{
    public int A { get; set; }
    public int B { get; init; }    // ZAMP012: B has no public setter.
}
```

Source fixtures: `UpdateInPlaceTests.ZAMP012_InitOnly_Destination_Reported`, `UpdateInPlaceTests.ZAMP012_Mixed_Settable_And_InitOnly_POCO_Reported`.

The fix is to switch the destination to a class with `{ get; set; }` properties — or, if the destination genuinely is immutable, drop the void overload and use the constructor form (which doesn't need setters because it goes through the constructor).

## `[TryMap]` Not Supported

Update-in-place is only available for `[Map]`, not `[TryMap]`. The reason is the aggregate-failure shape — list-pair update semantics are ambiguous (do you partially apply the successful elements? roll them back? leave the destination in a half-mutated state?), and there's no obvious right answer to bake into the generator.

If you need fallible update semantics, the recommended pattern is to construct via `TryMap(src)` first, then copy the result into your existing instance once you've verified success:

```csharp
var result = M.TryMap(updateRequest);
if (result.IsSuccess)
{
    var fresh = result.Value;
    existing.Id = fresh.Id;
    existing.Notes = fresh.Notes;
}
```

Or wrap the `Map(src, existingDst)` call in your own try/catch if the only failure mode you care about is exceptions from a custom hook.

## Where to Next

- **Hooks** — `[BeforeMap]`/`[AfterMap]` integrate cleanly with both the constructor and void forms. See [Hooks](hooks.md).
- **Cookbook** — end-to-end EF Core entity-mapping recipe in [Collection pipelines & EF Core entity mapping](cookbook/06-collection-pipelines.md).
