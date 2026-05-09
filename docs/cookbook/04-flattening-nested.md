---
id: cookbook-flattening-nested
title: Flattening nested DTOs
description: Use [MapProperty] dot-paths to project nested aggregates into flat row DTOs, with !. and ?. chain rules.
sidebar_position: 4
---

# Flattening nested DTOs

Reporting and table-rendering endpoints rarely want the full nested aggregate — they want a flat row whose columns came from several levels of the source graph. `[MapProperty("a.b.c", "Target")]` walks the path at compile time and inlines the access. The chain operator (`!.` vs `?.`) is decided by the destination's nullability, so the same path expression behaves correctly under both contracts.

## Scenario

The admin dashboard has two flat-row endpoints over the same nested order graph:

- **`/admin/orders`** — non-nullable row. The admin contract guarantees every order has a customer with an address; if any segment is null at runtime, that is a programming error and an `NRE` is the right outcome.
- **`/admin/orders/search`** — nullable row. Free-text search may return partially indexed records; missing segments must produce `null` columns rather than throw.

Same source graph, two destination shapes, two emission rules.

## Source types — nested graph

```csharp
public sealed record Address(string City, string Country);
public sealed record Customer(string Name, Address Address);
public sealed record Order(int Id, Customer Customer);
```

## Variant A — non-nullable destination

```csharp
public sealed record OrderRow(int Id, string CustomerName, string City);

[Map<Order, OrderRow>]
public static partial class OrderRowMappings
{
    [MapProperty("Customer.Name", "CustomerName")]
    [MapProperty("Customer.Address.City", "City")]
    public static partial OrderRow Map(Order src);
}
```

The destination columns are non-nullable strings. The generator emits a `!.` chain and treats null intermediates as a contract violation:

```csharp
public static OrderRow Map(Order src)
{
    ArgumentNullException.ThrowIfNull(src);
    var __dst = new OrderRow(
        Id:           src.Id,
        CustomerName: src.Customer!.Name,
        City:         src.Customer!.Address!.City
    );
    return __dst;
}
```

Runtime contract: if `src.Customer` or `src.Customer.Address` is null, the `!.` access throws `NullReferenceException`. That is by design — a non-nullable destination column declares that its source path is fully populated.

## Variant B — nullable destination

```csharp
public sealed record OrderSearchRow(int Id, string? CustomerName, string? City);

[Map<Order?, OrderSearchRow?>]
public static partial class OrderSearchMappings
{
    [MapProperty("Customer.Name", "CustomerName")]
    [MapProperty("Customer.Address.City", "City")]
    public static partial OrderSearchRow? Map(Order? src);
}
```

Both columns are nullable, so the generator emits a `?.` chain. A missing intermediate produces `null` instead of throwing:

```csharp
public static OrderSearchRow? Map(Order? src)
{
    if (src is null) return null;
    var __dst = new OrderSearchRow(
        Id:           src.Id,
        CustomerName: src.Customer?.Name,
        City:         src.Customer?.Address?.City
    );
    return __dst;
}
```

Same path expressions, different chain operator — driven entirely by the destination's nullable annotations.

## Endpoint wiring

```csharp
app.MapGet("/admin/orders", async (IOrderRepository repo, CancellationToken ct) =>
{
    var orders = await repo.GetAllAsync(ct);
    var rows = OrderRowMappings.Map(orders);          // List<OrderRow>
    return Results.Ok(rows);
});

app.MapGet("/admin/orders/search", async (string q, ISearchIndex idx, CancellationToken ct) =>
{
    var hits = await idx.SearchAsync(q, ct);          // may have null Customer/Address
    var rows = hits.Select(OrderSearchMappings.Map).ToList();
    return Results.Ok(rows);
});
```

## Diagnostic — ZAMP005

If the path references a property that does not exist, the generator reports **ZAMP005** at the first missing segment:

```csharp
[MapProperty("Customer.NoSuchProp.City", "City")]   // ZAMP005: 'NoSuchProp' not found on Customer
public static partial OrderRow Map(Order src);
```

The error points at the attribute, names the missing segment, and suggests the closest match if one exists. There is no runtime cost for typos — every path is resolved at compile time.

## Discussion

The `!.` vs `?.` decision is driven by the **destination column's** nullability, not the source path. If you want null-safety, declare the destination column as nullable. If you want the contract that the source path is always populated, declare the destination non-nullable and let `!.` enforce it.

See [Flattening](../flattening.md) for the full path-resolution algorithm, depth limits, and the table of which path shapes resolve to fields, properties, indexers, or method calls.
