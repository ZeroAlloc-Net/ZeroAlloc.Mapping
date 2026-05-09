---
id: cookbook-command-to-domain
title: Command → Domain
description: Map an ASP.NET request DTO to a domain aggregate root with smart-constructor value objects.
sidebar_position: 1
---

# Command → Domain

This recipe shows the most common mapping shape in a layered application: an HTTP request DTO arrives at a Minimal API endpoint, and the application service needs a fully-constructed domain aggregate to hand to the repository. ZeroAlloc.Mapping handles the trivial path with a single `[Map<,>]` attribute, and resolves the value-object wrapping (`int → OrderId`) automatically through the single-arg constructor convention.

## Scenario

You are building an order-management service. The transport layer speaks `PlaceOrderRequest` (a flat DTO from JSON body binding). The domain layer expects an `Order` aggregate with a strongly-typed `OrderId` value object. You want zero hand-written conversion code between the two and zero allocations beyond the destination record itself.

## Source type — HTTP DTO

```csharp
public sealed record PlaceOrderRequest(int Id, string Notes);
```

## Destination types — domain aggregate

```csharp
public readonly record struct OrderId(int Value);

public sealed record Order(OrderId Id, string Notes);
```

`OrderId` is a `readonly record struct` wrapping `int`. Its primary constructor takes a single `int` argument — that's the convention the generator looks for when it needs to convert `int → OrderId` automatically.

## Mapper declaration

```csharp
using ZeroAlloc.Mapping;

[Map<PlaceOrderRequest, Order>]
public static partial class OrderMappings { }
```

That's the entire mapper. No partial method body is needed — the `[Map<,>]` attribute alone produces the public `Map(PlaceOrderRequest)` overload.

## Endpoint wiring

```csharp
app.MapPost("/orders", async (PlaceOrderRequest req, IOrderRepository repo, CancellationToken ct) =>
{
    var order = OrderMappings.Map(req);
    await repo.SaveAsync(order, ct);
    return Results.Created($"/orders/{order.Id.Value}", order);
});
```

The endpoint is three lines: map, save, return. No `IMapper` indirection, no DI registration for the mapping itself — `OrderMappings.Map` is a static call resolved at compile time.

## What gets generated

The generator emits a body that constructs the destination directly. The `int → OrderId` conversion is auto-resolved via `OrderId`'s single-arg ctor:

```csharp
public static Order Map(PlaceOrderRequest src)
{
    ArgumentNullException.ThrowIfNull(src);
    var __dst = new Order(
        Id: new OrderId(src.Id),    // single-arg ctor convention
        Notes: src.Notes
    );
    return __dst;
}
```

Two things to notice:

1. **No reflection, no `IServiceProvider`.** The call site compiles to a direct constructor invocation.
2. **The wrapping is automatic.** You did not write `int → OrderId` anywhere. The generator scanned `OrderId` for a public single-argument constructor whose parameter type matches `int`, found one, and inlined the call.

## Discussion

The `int → OrderId` conversion is one of five paths the generator tries when source and destination types differ. The full ordering — identity → implicit conversion → single-arg ctor → static factory → nested mapper — is documented in [Basic Mapping](../basic-mapping.md).

This recipe assumes the smart constructor never throws. If `OrderId` validates its argument (e.g. rejects negative IDs), use `[TryMap]` instead so the failure surfaces as a `Result<Order, MappingError>` on the hot path rather than an uncaught exception. See [Recipe 03 — Fallible Mapping](03-fallible-with-smart-ctors.md) for that pattern.

For the inverse direction (`Order → OrderDto`), see [Recipe 02 — Domain to DTO](02-domain-to-dto.md).
