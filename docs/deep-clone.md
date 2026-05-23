---
id: deep-clone
title: Deep Clone
description: Whole-graph deep-clone emission via [Map(DeepClone = true)] without per-type [Map<,>] boilerplate.
sidebar_position: 16
---

# Deep Clone

Sometimes the right shape *is* a full deep clone — duplicating an aggregate before mutating it, snapshotting a state-tree for undo/redo, cloning a request DTO before passing it through a transformation pipeline that mutates in place. Declaring one `[Map<,>]` per reachable type works, but is tedious for graphs of more than three or four types. `DeepClone = true` walks the reachable type graph at generator time and emits literal `new T { ... }` clones for every type the outer `[Map<,>]` reaches.

## The Pattern

Add `DeepClone = true` to a single `[Map<TSrc, TSrc>]` declaration (source and destination are typically the same type for deep clones):

```csharp
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Address ShippingAddress { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
}

public sealed class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

[Map<Customer, Customer>(DeepClone = true)]
public static partial class CustomerClone { }
```

The generator walks `Customer` → `Address` and `Customer.Orders` → `Order`, emitting a `Map(Customer)`, `Map(Address)`, and `Map(Order)` automatically:

```csharp
public static Customer Map(Customer src)
{
    ArgumentNullException.ThrowIfNull(src);
    return new Customer
    {
        Id = src.Id,
        Name = src.Name,
        ShippingAddress = Map(src.ShippingAddress),
        Orders = src.Orders is null
            ? null!
            : Enumerable.ToList(Enumerable.Select(src.Orders, __e => __e is null ? null! : new Order { /* ... */ }))
    };
}

public static Address Map(Address src)
{
    ArgumentNullException.ThrowIfNull(src);
    return new Address
    {
        Street = src.Street,
        City = src.City
    };
}

public static Order Map(Order src)
{
    ArgumentNullException.ThrowIfNull(src);
    return new Order
    {
        Id = src.Id,
        Total = src.Total
    };
}
```

The generated method set is identical to what you'd get by declaring three explicit `[Map<X, X>]`s, minus the boilerplate. The walk terminates on primitives (`int`, `string`, `decimal`, `DateTime`, etc.), enums, and any type that already has an explicit `[Map<,>]` declared on the same class — that explicit declaration *wins*, and `DeepClone` stops walking through it.

## Explicit Nested Mappings Take Over

When a sibling `[Map<Inner, Inner>]` is already declared, `DeepClone` doesn't override it — the explicit mapping is preserved as-is and the deep-clone walker treats `Inner` as a leaf:

```csharp
[Map<Address, Address>]
public static partial class CustomerClone
{
    public static partial Address Map(Address src);  // hand-written, e.g. for normalisation
}

[Map<Customer, Customer>(DeepClone = true)]
public static partial class CustomerClone { }
```

The walker emits `Map(Customer)` that calls into the hand-written `Map(Address)` — useful when one inner type needs special-case logic (normalisation, computed fields, validation) but the rest of the graph is a straightforward clone.

## Cloneability Requirements — ZAMP019

For each type the walker reaches, it must be able to emit `new T { Prop = src.Prop, ... }`. That requires the type to have either:

- A public parameterless constructor (or no explicit constructors), and settable properties (`{ get; set; }`) for every cloned property, **or**
- An accessible primary constructor / record positional constructor whose parameters cover every cloned property (in which case `new T(p1, p2, ...)` is emitted instead).

Types that are abstract, have no accessible constructor, or have init-only properties without a matching constructor parameter fire **[ZAMP019](diagnostics.md#zamp019--mapdeepclone--true-reaches-an-uncloneable-type)**:

```csharp
public abstract class AbstractBase { public int Id { get; set; } }
public sealed class Holder { public AbstractBase Inner { get; set; } = null!; }

[Map<Holder, Holder>(DeepClone = true)]
public static partial class M { }
// ZAMP019: deep-clone reaches AbstractBase, which is abstract and uncloneable.
```

The fix is either to declare an explicit `[Map<AbstractBase, AbstractBase>]` (e.g. dispatch via `[PolymorphicMap]` for the abstract case) that takes over the walk, or to change the shape of the reachable type so it can be cloned mechanically.

## Composing with CycleSafe — ZAMP020

`DeepClone` walks the type graph by *type*, not by runtime instance. A cyclic type graph — `Customer` has `Order`s, each `Order` references `Customer` — cycles back to itself during the walk. Without runtime tracking, the emitted code would recurse infinitely:

```csharp
public sealed class Customer { public List<Order> Orders { get; set; } = new(); }
public sealed class Order { public Customer Customer { get; set; } = null!; }

[Map<Customer, Customer>(DeepClone = true)]
public static partial class M { }
// ZAMP020: deep-clone walks a cyclic type graph (cycle through Order) without CycleSafe = true.
```

The fix is to compose `DeepClone = true` with `CycleSafe = true`:

```csharp
[Map<Customer, Customer>(DeepClone = true, CycleSafe = true)]
public static partial class M { }
```

**What actually happens today.** When both `DeepClone = true` and `CycleSafe = true` are set on the same `[Map<,>]`, the **CycleSafe** emit path wins on the routing fork. The cycle-safe pair (entry method + recursive tracker-accepting overload) is used; reachable reference-typed properties WITHOUT an explicit nested `[Map<,>]` declaration are NOT deep-cloned — they fall through to the standard conversion path (alias, not literal clone). To deep-clone such properties, the user must declare an explicit nested `[Map<,>]` for each one. CycleSafe's transitive ZAMP018 enforcement will catch any missing nested declarations.

Full integration of deep-clone literal walks WITH tracker threading is deferred to a future release. See `docs/backlog.md` item `B12 — DeepClone + CycleSafe integration`.

Note this still requires settable properties on every cloned type — see [Cycle-Safe Mapping](cycle-safe-mapping.md#why-settable-properties-are-required).

## Diagnostics

### ZAMP019 (Error) — uncloneable type

Fires when the deep-clone walk reaches a type with no public parameterless constructor and no fully-covering constructor for its mapped properties. See [ZAMP019](diagnostics.md#zamp019--mapdeepclone--true-reaches-an-uncloneable-type).

### ZAMP020 (Error) — cyclic type graph without CycleSafe

Fires when the deep-clone walk hits a cycle in the type graph and the declaration is not also `CycleSafe = true`. See [ZAMP020](diagnostics.md#zamp020--mapdeepclone--true-walks-a-cyclic-type-graph-without-cyclesafe--true).

## Where to Next

- **Cycle-Safe Mapping** — required companion when the type graph cycles. See [Cycle-Safe Mapping](cycle-safe-mapping.md).
- **IQueryable Projection** — orthogonal feature; projection mappings can't be deep-clone (the expression tree has no walker). See [IQueryable Projection](iqueryable-projection.md).
- **Performance** — deep-clone emission is allocation-bounded by the destination graph; there's no reflection cost. See [Performance](performance.md).
