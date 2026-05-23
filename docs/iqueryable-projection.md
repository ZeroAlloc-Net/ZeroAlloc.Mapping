---
id: iqueryable-projection
title: IQueryable Projection
description: Opt-in Expression<Func<TSrc, TDst>> emission via [Map(Projection = true)] for EF Core projections.
sidebar_position: 14
---

# IQueryable Projection

EF Core and any other LINQ provider that translates `IQueryable<T>` to SQL needs the mapping as an `Expression<Func<TSrc, TDst>>` — not a compiled method. Reflection-based mappers (AutoMapper's `ProjectTo`) build that expression at runtime; `ZeroAlloc.Mapping` opts you into a static one, emitted at compile time, that the C# compiler validates and EF Core's translator can walk.

## The Pattern

Add `Projection = true` to the `[Map<,>]` declaration:

```csharp
public sealed record Order(int Id, string CustomerName, decimal Total);
public sealed record OrderDto(int Id, string CustomerName, decimal Total);

[Map<Order, OrderDto>(Projection = true)]
public static partial class OrderMappings { }
```

The generator emits the usual `OrderDto Map(Order)` plus a static property:

```csharp
public static System.Linq.Expressions.Expression<System.Func<Order, OrderDto>> Projection { get; } =
    src => new OrderDto(src.Id, src.CustomerName, src.Total);
```

Consumed straight from EF Core:

```csharp
var dtos = await db.Orders
    .Where(o => o.Total > 100m)
    .Select(OrderMappings.Projection)
    .ToListAsync();
```

EF Core's LINQ translator walks the expression and turns the whole pipeline into a single `SELECT Id, CustomerName, Total FROM Orders WHERE Total > 100` — no materialisation of `Order`, no per-row `Map(Order)` call.

## Nested Mappings Auto-Inline

When the projected type has a property whose mapping is declared as a sibling `[Map<,>]`, the generator inlines the nested expression into the outer `Projection` automatically:

```csharp
public sealed record Customer(int Id, string Name);
public sealed record CustomerDto(int Id, string Name);
public sealed record Order(int Id, Customer Customer, decimal Total);
public sealed record OrderDto(int Id, CustomerDto Customer, decimal Total);

[Map<Customer, CustomerDto>(Projection = true)]
[Map<Order, OrderDto>(Projection = true)]
public static partial class M { }
```

The emitted `Order` projection becomes:

```csharp
public static Expression<Func<Order, OrderDto>> Projection { get; } =
    src => new OrderDto(src.Id, new CustomerDto(src.Customer.Id, src.Customer.Name), src.Total);
```

The inlining is transitive — `CustomerDto` doesn't need to be reachable through a property reference; the compiler sees the literal `new CustomerDto(...)` and EF Core translates it as a single SQL projection.

## In-Memory Composability

`OrderMappings.Projection` is just an `Expression<Func<,>>`, so it composes with LINQ-to-Objects too:

```csharp
var compiled = OrderMappings.Projection.Compile();
IEnumerable<OrderDto> dtos = orders.Select(compiled);
```

Compiling once and reusing is the right pattern — `Expression.Compile()` is a non-trivial JIT-emit pass per invocation.

## Constraints — Incompatible Features

Projection trades runtime customisation for static expression purity. The following features fire **[ZAMP017](diagnostics.md#zamp017--mapprojection--true-uses-a-feature-ef-core-cannot-translate)** when declared on the same class as a `Projection = true` mapping:

| Feature | Why incompatible |
|---|---|
| `[BeforeMap]` / `[AfterMap]` hooks | Hooks are method calls; EF Core can't translate them to SQL. |
| `[MappingCulture]` | The culture string would need to be embedded as a literal in the expression tree, but `Parse` calls themselves don't survive EF Core's translation. |
| `[PolymorphicMap<,>]` | The `switch` dispatcher doesn't translate; EF Core uses `OfType<T>()` for discrimination instead. |

Declare projection-eligible mappings on a separate `static partial class` from your hook-bearing / culture-bearing ones if you need both shapes.

## AOT Note

The `Projection` property's expression-tree initializer is a literal lambda — the C# compiler emits it as a static `Expression<>` constant, and it survives AOT publish intact.

However, *executing* the projection on an arbitrary `IEnumerable<TSrc>` via `Queryable.AsQueryable<T>().Select(...)` requires JIT dynamic-code support and triggers AOT trim warnings IL2026 / IL3050:

```csharp
// On a native-AOT host, this triggers IL2026/IL3050.
var compiled = OrderMappings.Projection.Compile();
return orders.AsQueryable().Select(OrderMappings.Projection);
```

EF Core consumers get expression-tree execution naturally — the EF runtime runs on JIT, so the warnings don't apply in practice. AOT consumers (Native AOT, NativeAOT-published web apps) cannot directly invoke the projection without either disabling the AOT errors or pre-compiling the expression into a `Func<,>` at app start.

The recommended AOT-safe pattern: compile once, store the `Func<,>`, and use it with LINQ-to-Objects.

```csharp
private static readonly Func<Order, OrderDto> _compiled = OrderMappings.Projection.Compile();

public static IEnumerable<OrderDto> ProjectAll(IEnumerable<Order> orders) =>
    orders.Select(_compiled);
```

`Expression.Compile()` itself is JIT-only — there's no escape from that on Native AOT short of pre-baking the projection through the source generator pipeline at app build, which is out of scope for v1.4.

## Diagnostics

### ZAMP017 (Error) — incompatible feature

Fires when `Projection = true` coexists with any of `[BeforeMap]`, `[AfterMap]`, `[MappingCulture]`, or `[PolymorphicMap<,>]` on the same class. Also fires transitively when a nested mapping inlined into the projection violates the same constraint. See [ZAMP017](diagnostics.md#zamp017--mapprojection--true-uses-a-feature-ef-core-cannot-translate).

## Where to Next

- **Cycle-Safe Mapping** — ORM aggregates with back-references. See [Cycle-Safe Mapping](cycle-safe-mapping.md).
- **Deep Clone** — full deep-clone semantics for type graphs. See [Deep Clone](deep-clone.md).
- **Performance** — projection bypasses the per-row `Map(TSrc)` call entirely. See [Performance](performance.md).
