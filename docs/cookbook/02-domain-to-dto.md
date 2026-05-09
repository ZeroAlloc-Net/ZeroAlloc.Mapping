---
id: cookbook-domain-to-dto
title: Domain → DTO
description: Symmetric domain ↔ DTO mapping with [ReverseMap] for read and write CRUD endpoints.
sidebar_position: 2
---

# Domain → DTO

Every CRUD endpoint pair has the same shape: `GET` returns a domain entity as a DTO, `PUT` accepts the same DTO and persists the corresponding domain entity. When the two types match by-name, `[ReverseMap<,>]` declares both directions in a single attribute.

## Scenario

You expose an `Order` aggregate over HTTP. The wire shape (`OrderDto`) is field-identical to the domain shape — same names, same primitive types — so neither side needs custom property mappings. You want the same mapper class to serve both the read endpoint (`GET /orders/{id}`) and the write endpoint (`PUT /orders/{id}`).

## Types and mapper

```csharp
public sealed record Order(int Id, string Notes);
public sealed record OrderDto(int Id, string Notes);

[ReverseMap<Order, OrderDto>]
public static partial class OrderApiMappings { }
```

`[ReverseMap<,>]` is equivalent to writing `[Map<Order, OrderDto>]` and `[Map<OrderDto, Order>]` together. The generator emits both `Map(Order)` and `Map(OrderDto)` overloads, plus collection overloads for each direction (see [Recipe 06](06-collection-pipelines.md) for the collection-overload usage).

## Endpoint wiring

```csharp
app.MapGet("/orders/{id:int}", async (int id, IOrderRepository repo, CancellationToken ct) =>
{
    var order = await repo.GetAsync(id, ct);
    return order is null
        ? Results.NotFound()
        : Results.Ok(OrderApiMappings.Map(order));        // domain → DTO
});

app.MapPut("/orders/{id:int}", async (int id, OrderDto dto, IOrderRepository repo, CancellationToken ct) =>
{
    var order = OrderApiMappings.Map(dto);                // DTO → domain
    await repo.UpdateAsync(order, ct);
    return Results.NoContent();
});
```

Both endpoints call `OrderApiMappings.Map`. The compiler picks the right overload by argument type — there is no runtime dispatch.

## What gets generated

For the declaration above, the generator emits four public methods on `OrderApiMappings`:

```csharp
public static OrderDto Map(Order src);
public static Order   Map(OrderDto src);

public static List<OrderDto> Map(List<Order> src);
public static List<Order>    Map(List<OrderDto> src);
```

(Plus the `IEnumerable`/array variants. See [Collections](../collections.md) for the full overload table.)

## Limitation — ZAMP009

`[ReverseMap]` only produces a safe round-trip when the two types are field-identical by-name. If you need any per-property customisation, the generator cannot decide which direction the customisation applies to, so it emits **ZAMP009 (Error)**.

The following will fail to compile:

```csharp
[ReverseMap<Order, OrderDto>]
public static partial class OrderApiMappings
{
    [MapProperty("Notes", "Description")]               // ZAMP009
    public static partial OrderDto Map(Order src);
}
```

Fix: split into two separate `[Map<,>]` declarations and put the customisation on the direction that needs it.

```csharp
[Map<Order, OrderDto>]
[Map<OrderDto, Order>]
public static partial class OrderApiMappings
{
    [MapProperty("Notes", "Description")]
    public static partial OrderDto Map(Order src);

    [MapProperty("Description", "Notes")]
    public static partial Order Map(OrderDto src);
}
```

The same rule applies to `[MapValue]` and `[MapperIgnoreTarget]` — any attribute that customises one direction breaks the symmetry that `[ReverseMap]` relies on.

## Discussion

`[ReverseMap]` is the right tool when source and destination shapes are field-identical and you want a single source of truth. The moment you need divergence, drop down to two `[Map<,>]` declarations — the cost is one extra attribute, the gain is unambiguous customisation per direction.

See [Reverse Mapping](../reverse-mapping.md) for the full attribute reference and the round-trip test pattern. For collection-overload usage in bulk endpoints, see [Recipe 06 — Collection Pipelines](06-collection-pipelines.md).
