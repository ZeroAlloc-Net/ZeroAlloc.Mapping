---
id: cycle-safe-mapping
title: Cycle-Safe Mapping
description: Runtime cycle-breaking via [Map(CycleSafe = true)] for ORM aggregates with back-references.
sidebar_position: 15
---

# Cycle-Safe Mapping

ORM aggregates and event-sourced domains routinely carry back-references — a `Customer` has many `Order`s, each `Order` has a `Customer` reference back to its owner. Mapping that graph with the default `[Map<,>]` recurses until the stack blows. `CycleSafe = true` threads an `IDictionary<object, object>` tracker through the recursion and reuses already-seen destination instances, breaking cycles in O(1) per node.

## The Pattern

Add `CycleSafe = true` to the `[Map<,>]` declaration:

```csharp
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
}

public sealed class Order
{
    public int Id { get; set; }
    public Customer Customer { get; set; } = null!;  // back-reference
}

public sealed class CustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<OrderDto> Orders { get; set; } = new();
}

public sealed class OrderDto
{
    public int Id { get; set; }
    public CustomerDto Customer { get; set; } = null!;
}

[Map<Customer, CustomerDto>(CycleSafe = true)]
[Map<Order, OrderDto>(CycleSafe = true)]
public static partial class Mappings { }
```

The generator emits a paired entry + recursive overload per mapping:

```csharp
public static CustomerDto Map(Customer src)
{
    ArgumentNullException.ThrowIfNull(src);
    var __tracker = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
    return Map(src, __tracker);
}

internal static CustomerDto Map(Customer src, IDictionary<object, object> tracker)
{
    if (tracker.TryGetValue(src, out var __existing)) return (CustomerDto)__existing;
    var __dst = new CustomerDto();
    tracker[src] = __dst;
    __dst.Id = src.Id;
    __dst.Name = src.Name;
    __dst.Orders = src.Orders is null
        ? null!
        : Enumerable.ToList(Enumerable.Select(src.Orders, x => x is null ? null! : Map(x, tracker)));
    return __dst;
}
```

The entry overload always allocates a fresh tracker — there is no public surface that exposes the tracker. The recursive overload is `internal`, so cycle-safe mappings on the same class can call each other but external callers always go through the entry method. The tracker uses `ReferenceEqualityComparer.Instance` to identify objects by reference (not by `Equals`), which matches what ORM identity-maps do.

## Why Settable Properties Are Required

Cycle-breaking depends on a two-step sequence: allocate the destination, register it in the tracker, *then* populate properties. If the destination has a constructor that takes all the properties (records, positional types), the tracker can't be populated before the recursive call into a child that references back to the parent — the parent isn't constructed yet.

The generator therefore requires that the destination type has:

- A public parameterless constructor (or no explicit constructors, so the implicit one applies), and
- Settable properties (`{ get; set; }`) for every mapped destination property.

Records, positional records, and types with required constructor parameters are rejected by the destination-shape check during cycle-safe emit. Practically: if you want cycle-safe mapping, use POCOs with settable properties on the destination side. The source side is unconstrained — source can be a record, a POCO, anything readable.

## Transitive Enforcement — ZAMP018

Cycle-safety is contagious. If `Customer → CustomerDto` is cycle-safe but the nested `Order → OrderDto` mapping isn't, the recursive call into `Map(Order)` would lose the tracker and the cycle would still blow the stack. The generator catches this at compile time and fires **[ZAMP018](diagnostics.md#zamp018--mapcyclesafe--true-references-a-non-cyclesafe-nested-mapping)**:

```csharp
[Map<Customer, CustomerDto>(CycleSafe = true)]
[Map<Order, OrderDto>]   // ZAMP018: referenced by a CycleSafe mapping but not CycleSafe itself.
public static partial class Mappings { }
```

The fix is to mark every mapping reachable from a `CycleSafe = true` declaration as `CycleSafe = true` itself, or to break the recursive reference (e.g. ignore the `Customer.Orders` collection so the back-reference doesn't surface).

The check is transitive — `A → B → C` where `A` and `B` are cycle-safe and `C` isn't fires ZAMP018 on `C`, not on `B`.

## Self-Cycles

A type that references itself (a tree node with a `Parent` back-reference, or a graph node with `Neighbours`) works the same way — the tracker catches the self-reference on the recursive call:

```csharp
public sealed class TreeNode
{
    public int Id { get; set; }
    public TreeNode? Parent { get; set; }
    public List<TreeNode> Children { get; set; } = new();
}

public sealed class TreeNodeDto
{
    public int Id { get; set; }
    public TreeNodeDto? Parent { get; set; }
    public List<TreeNodeDto> Children { get; set; } = new();
}

[Map<TreeNode, TreeNodeDto>(CycleSafe = true)]
public static partial class TreeMappings { }
```

A node-with-parent and `parent.Children` containing the node round-trips correctly — the recursive call into `Map(Children[i])` finds the parent in the tracker and reuses the instance.

## Allocation Cost

The tracker is one `Dictionary<object, object>` allocation per entry-call. For shallow non-cyclic graphs this is pure overhead; for deep cyclic graphs it's the only thing that lets the mapping terminate. Per-node cost beyond the tracker allocation is one `TryGetValue` + one `tracker[src] = __dst` assignment — both O(1) amortised.

If you have a hot path that maps a known-acyclic shape and you want to skip the tracker overhead, declare a separate non-cycle-safe `[Map<,>]` on a different host class.

## Combining with `DeepClone`

When `[Map(DeepClone = true, CycleSafe = true)]` is declared, the generator emits a unified clone walk that combines both flags' guarantees: **every reachable reference type is cloned**, and **every type in the graph participates in runtime cycle resolution**.

Reachable reference types fall into two categories:

- **Types with a public parameterless constructor** — the generator emits a private static `__CloneCycleSafe_<TypeName>` helper that allocates, registers the new instance in the tracker, then walks children. Cycles through these types are resolved at runtime.
- **Primary-ctor-only types (records, immutables)** — the generator can't register a half-built instance in the tracker before evaluating ctor arguments. If such a type is reachable but NOT part of a cycle, it's cloned inline (`new T(arg1: ..., arg2: ...)`). If it IS part of a cycle, the generator emits **[ZAMP021](diagnostics.md#zamp021--deepclone--cyclesafe-reaches-a-primary-ctor-only-type-in-a-cycle)** at compile time.

```csharp
public sealed class Node
{
    public string Name { get; set; } = "";
    public Node? Next { get; set; }
}

[Map<Node, Node>(DeepClone = true, CycleSafe = true)]
public static partial class Mappers { }

// Caller:
var a = new Node { Name = "a" };
var b = new Node { Name = "b" };
a.Next = b; b.Next = a;          // A → B → A cycle.

var cloned = Mappers.Map(a);
// cloned.Next.Next is cloned (the tracker resolved the cycle).
```

`ZAMP021` example:

```csharp
public sealed record Box(string Label, Box? Inner);

[Map<Box, Box>(DeepClone = true, CycleSafe = true)]
public static partial class Mappers { }   // → ZAMP021: Box has no parameterless ctor and is cyclic.
```

Fix by adding a parameterless ctor (`public Box() : this("", null) { }`), declaring an explicit nested `[Map<Box, Box>]` (and accepting its constraints), or dropping `CycleSafe = true`.

## Diagnostics

### ZAMP018 (Error) — non-CycleSafe nested mapping

Fires when a `CycleSafe = true` mapping references a nested `[Map<,>]` (declared on the same class) that isn't `CycleSafe = true`. See [ZAMP018](diagnostics.md#zamp018--mapcyclesafe--true-references-a-non-cyclesafe-nested-mapping).

The companion diagnostic for deep-clone walks of cyclic type graphs is **[ZAMP020](diagnostics.md#zamp020--mapdeepclone--true-walks-a-cyclic-type-graph-without-cyclesafe--true)** — see [Deep Clone](deep-clone.md) for how cycle-safety composes with deep-clone emission. When both flags are set, **[ZAMP021](diagnostics.md#zamp021--deepclone--cyclesafe-reaches-a-primary-ctor-only-type-in-a-cycle)** fires if the combined walk reaches a primary-ctor-only type that participates in a cycle.

## Where to Next

- **Deep Clone** — `CycleSafe = true` composes with `DeepClone = true` for cyclic graphs that need full clone semantics. See [Deep Clone](deep-clone.md).
- **IQueryable Projection** — projection mappings can't be cycle-safe (the expression tree has no place to thread a tracker). See [IQueryable Projection](iqueryable-projection.md).
- **Performance** — tracker-allocation cost relative to the no-tracker baseline. See [Performance](performance.md).
