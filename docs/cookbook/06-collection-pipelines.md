---
id: cookbook-collection-pipelines
title: Collection pipelines
description: Bulk import, fallible bulk validation, EF Core update-in-place, and IEnumerable streaming with auto-emitted collection overloads.
sidebar_position: 6
---

# Collection pipelines

Every `[Map<,>]` declaration produces collection overloads automatically: `List<T>`, `IEnumerable<T>`, and array variants. This recipe walks four real shapes — bulk import, fallible bulk, EF Core update-in-place, and lazy streaming — that cover most production needs without writing a single mapping body.

## Scenario A — bulk import

A CSV upload imports up to a thousand orders in one HTTP request. The endpoint receives a `List<OrderRow>` parsed by the JSON binder and needs a `List<Order>` to hand to the repository's `AddRangeAsync`.

### Setup

```csharp
public sealed record OrderRow(int Id, string Notes);
public sealed record Order(int Id, string Notes);

[Map<OrderRow, Order>]
public static partial class BulkImportMappings { }
```

### Endpoint

```csharp
app.MapPost("/orders/bulk", async (List<OrderRow> rows, IOrderRepository repo, CancellationToken ct) =>
{
    List<Order> orders = BulkImportMappings.Map(rows);   // capacity-presized List
    await repo.AddRangeAsync(orders, ct);
    return Results.Ok(new { imported = orders.Count });
});
```

The `Map(List<OrderRow>)` overload is auto-emitted. The result list is constructed with `new List<Order>(rows.Count)` so the backing array is sized once and never reallocated during the loop. See the allocation-budget table in [Performance](../performance.md) for the exact shape.

## Scenario B — fallible bulk

When rows can be invalid (e.g. one of the columns is a smart-ctor value object that throws), use `[TryMap]` so each row's failure is captured rather than aborting the batch on the first bad input.

```csharp
[TryMap<OrderRow, Order>]
public static partial class BulkImportTryMappings { }
```

```csharp
app.MapPost("/orders/bulk/validated", async (List<OrderRow> rows, IOrderRepository repo, CancellationToken ct) =>
{
    var result = BulkImportTryMappings.TryMap(rows);
    if (!result.IsSuccess)
    {
        // result.Error.Code == "mapping.collection.elements_failed"
        // result.Error.Children — per-row failures with PropertyPath like "[3].Notes"
        return Results.BadRequest(SerialiseFailureTree(result.Error));
    }

    await repo.AddRangeAsync(result.Value, ct);
    return Results.Ok(new { imported = result.Value.Count });
});
```

The aggregate error code is the stable identifier for batch failures. Each child carries the `[i]` PropertyPath segment so the UI can highlight which rows need correction. See [Recipe 03](03-fallible-with-smart-ctors.md) for the per-element error shape.

## Scenario C — EF Core entity update

For mutable entities owned by `DbContext`, do not allocate a new entity — update the existing tracked instance so EF Core's change tracker sees only the modified columns. ZeroAlloc.Mapping's update-in-place overload writes directly into a destination you supply.

```csharp
public sealed class OrderEntity
{
    public int Id { get; set; }
    public string Notes { get; set; } = "";
}

public sealed record OrderUpdateRequest(int Id, string Notes);

[Map<OrderUpdateRequest, OrderEntity>]
public static partial class OrderUpdateMappings
{
    public static partial void Map(OrderUpdateRequest src, OrderEntity existingDst);
}
```

The partial method declaration with an `(src, dst)` signature signals update-in-place. The generator emits property assignments rather than a constructor call:

```csharp
public static partial void Map(OrderUpdateRequest src, OrderEntity existingDst)
{
    ArgumentNullException.ThrowIfNull(src);
    ArgumentNullException.ThrowIfNull(existingDst);
    existingDst.Id    = src.Id;
    existingDst.Notes = src.Notes;
}
```

### Endpoint

```csharp
app.MapPut("/orders/{id:int}", async (int id, OrderUpdateRequest req, AppDbContext db, CancellationToken ct) =>
{
    var existing = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
    if (existing is null) return Results.NotFound();

    OrderUpdateMappings.Map(req, existing);   // EF detects field changes
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});
```

EF Core's change tracker observes the property-set operations and emits an `UPDATE` statement that touches only the modified columns. There is no entity allocation, no detach/attach dance, and no risk of identity-map collisions.

## Scenario D — IEnumerable streaming

For very large reads where the full list cannot live in memory, the `Map(IEnumerable<T>)` overload returns a lazy `IEnumerable<T>` that maps each element on demand:

```csharp
IEnumerable<Order> orders = BulkImportMappings.Map(repo.StreamAll());   // lazy
foreach (var order in orders)
{
    await ProcessAsync(order);
}
```

The streaming overload is implemented as `source.Select(Map)` with no intermediate buffer. Allocation at the call site is zero — one `IEnumerable` adapter per call, amortised to nothing across a long iteration. The per-element cost is whatever the scalar `Map` allocates (typically a single destination record).

This is the right shape for ETL pipelines, server-sent events, and any read path where you control the iteration cadence and do not need random access.

## Discussion

The four shapes above cover the spectrum of collection ergonomics:

| Shape           | Allocation               | Use case                                  |
| --------------- | ------------------------ | ----------------------------------------- |
| `List<T>`       | One presized list        | Bulk endpoints, repository inputs         |
| `Result<List>`  | One list + error tree    | Fallible bulk validation                  |
| Update-in-place | Zero entity allocations  | EF Core, mutable domain aggregates        |
| `IEnumerable`   | Zero at call site        | Streaming, ETL, server-sent events        |

See [Collections](../collections.md) for the full overload table including arrays, `IReadOnlyList`, and `ImmutableArray`. See [Update-in-Place](../update-in-place.md) for the property-set emission rules and EF Core integration notes. The allocation budget for each shape is pinned by the snapshot tests documented in [Performance](../performance.md).
