---
id: cookbook-polymorphic-domain
title: Polymorphic domain hierarchies
description: Use [PolymorphicMap] to dispatch a single Map call across a sealed-derived class hierarchy.
sidebar_position: 5
---

# Polymorphic domain hierarchies

Domain types modelled as a closed hierarchy — one abstract base, several sealed derived records — are the natural shape for things like payment methods, notification channels, and event types. The wire format mirrors that hierarchy as a discriminated union. `[PolymorphicMap<,>]` emits a single dispatcher that routes each runtime type to the matching per-leaf mapping, so the calling code never has to switch on type.

## Scenario

The payment-processing service stores payment methods as a sealed-derived hierarchy. The HTTP API serialises them as a discriminated-union DTO. Wherever the application returns a payment method, the call site does not know — and should not need to know — which concrete subtype it has. A single `Map(method)` call must produce the right DTO leaf at runtime.

## Domain types

```csharp
public abstract record PaymentMethod;
public sealed record CreditCard(string Last4, string Network) : PaymentMethod;
public sealed record BankTransfer(string Iban, string Bic)    : PaymentMethod;
public sealed record CryptoWallet(string Address, string Chain) : PaymentMethod;
```

## DTO types

```csharp
public abstract record PaymentMethodDto;
public sealed record CreditCardDto(string Last4, string Network)   : PaymentMethodDto;
public sealed record BankTransferDto(string Iban, string Bic)      : PaymentMethodDto;
public sealed record CryptoWalletDto(string Address, string Chain) : PaymentMethodDto;
```

The DTO hierarchy mirrors the domain hierarchy one-for-one. That is the precondition `[PolymorphicMap]` requires — every domain leaf must have a corresponding DTO leaf with a per-leaf mapping.

## Mapper

```csharp
[Map<CreditCard, CreditCardDto>]
[Map<BankTransfer, BankTransferDto>]
[Map<CryptoWallet, CryptoWalletDto>]
[PolymorphicMap<PaymentMethod, PaymentMethodDto>]
public static partial class PaymentMappings { }
```

Three per-leaf `[Map<,>]` declarations plus one `[PolymorphicMap<,>]` over the abstract bases. The order of attributes does not matter — the generator collects all per-leaf mappings first, then synthesises the dispatcher.

## Usage

```csharp
PaymentMethod method = await repo.GetMethodAsync(userId);
PaymentMethodDto dto = PaymentMappings.Map(method);    // dispatches per runtime type
```

The call site is type-blind. Whether `method` is a `CreditCard`, `BankTransfer`, or `CryptoWallet`, the dispatcher picks the right per-leaf mapping in a single switch.

## Generated body

The dispatcher is a pattern-matching switch with one arm per registered leaf:

```csharp
public static PaymentMethodDto Map(PaymentMethod src)
{
    ArgumentNullException.ThrowIfNull(src);
    return src switch
    {
        CreditCard   __0 => Map(__0),
        BankTransfer __1 => Map(__1),
        CryptoWallet __2 => Map(__2),
        _ => throw new InvalidOperationException(
                $"No polymorphic mapping registered for runtime type {src.GetType().FullName}.")
    };
}
```

The fallback `throw` fires only if a new derived type is added to the hierarchy without a corresponding `[Map<,>]` registration — see ZAMP013/014/015 below for the compile-time guards that catch this earlier. The exact emission shape is documented under [Polymorphic Dispatch](../polymorphic.md).

## Endpoint wiring

```csharp
app.MapGet("/users/{id:guid}/payment-method", async (Guid id, IPaymentRepo repo, CancellationToken ct) =>
{
    PaymentMethod method = await repo.GetMethodAsync(id, ct);
    return Results.Ok(PaymentMappings.Map(method));
});
```

The endpoint is one line of mapping regardless of how many concrete subtypes exist. Adding a new `WirePayment` leaf is purely additive: declare `WirePayment`, declare `WirePaymentDto`, add `[Map<WirePayment, WirePaymentDto>]`. The dispatcher picks it up automatically.

## Collection variant

`Map(List<PaymentMethod>) → List<PaymentMethodDto>` is auto-emitted for any `[PolymorphicMap]`. A mixed-type list dispatches per-element:

```csharp
List<PaymentMethod> methods = await repo.GetUserMethodsAsync(userId, ct);
List<PaymentMethodDto> dtos = PaymentMappings.Map(methods);
```

Each list element walks the same switch — there is no per-list-type specialisation, so a list containing all three subtypes mixes seamlessly.

## `[PolymorphicTryMap]` variant

For boundaries where any per-leaf mapping is fallible (e.g. one of the subtypes uses a smart-ctor value object), declare `[PolymorphicTryMap<,>]` instead. The dispatcher returns `Result<PaymentMethodDto, MappingError>` and propagates the failing leaf's error unchanged. See [Polymorphic Dispatch](../polymorphic.md) for the full attribute table.

## Discussion

Three diagnostics guard the polymorphic shape at compile time:

- **ZAMP013** — `[PolymorphicMap]` declared but no per-leaf `[Map<,>]` exists for at least one sealed-derived type.
- **ZAMP014** — base type is not abstract or has no sealed-derived members reachable in the compilation.
- **ZAMP015** — destination hierarchy does not mirror the source hierarchy (a leaf has no DTO counterpart).

See [Diagnostics](../diagnostics.md) for the full ZAMP000–016 reference. For the dispatcher emission shape and performance characteristics (per-element switch is a JIT-friendly type-test sequence, no virtual call), see [Polymorphic Dispatch](../polymorphic.md).
