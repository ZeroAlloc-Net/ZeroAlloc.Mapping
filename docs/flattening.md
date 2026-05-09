---
id: flattening
title: Flattening
description: Map nested source paths to flat destination properties via dotted [MapProperty] expressions.
sidebar_position: 3
---

# Flattening

Most DTOs are flatter than the domain types they project from. `Order.Customer.Address.City` lives three references deep on the domain side, but the API contract is a single `City` string on the wire. Flattening is the generator's term for walking that dotted path and emitting the right null-handling chain into the destination assignment.

## The Pattern

`[MapProperty]` accepts a dotted path on the source side. Add a user-declared partial method on the mapper class to attach the attribute to.

```csharp
public sealed record Address(string City);
public sealed record Customer(Address Address);
public sealed record OrderRequest(Customer Customer);
public sealed record OrderDto(string City);

[Map<OrderRequest, OrderDto>]
public static partial class M
{
    [MapProperty("Customer.Address.City", "City")]
    public static partial OrderDto Map(OrderRequest src);
}
```

Generated body:

```csharp
public static partial global::OrderDto Map(global::OrderRequest src)
{
    global::System.ArgumentNullException.ThrowIfNull(src);
    var __dst = new global::OrderDto(
        City: src.Customer!.Address!.City
    );
    return __dst;
}
```

The walk is purely syntactic property-name resolution against the type at each segment — there's no reflection at runtime, no dictionary lookup, just a chained member-access expression.

## Null-Handling Rule

The generator picks `!.` (null-forgiving) or `?.` (null-conditional) based on the **destination's** nullability, not the source's. This is the rule worth memorising:

- **Non-nullable destination** → `!.` chain. Throws `NullReferenceException` at runtime if any intermediate segment is null.
- **Nullable destination** → `?.` chain. Result is `null` if any intermediate segment is null.

### Non-nullable destination → `!.`

```csharp
public sealed record Dst(string City);  // non-nullable

[MapProperty("Customer.Address.City", "City")]
// Emits: City: src.Customer!.Address!.City
```

:::warning Runtime contract
The `!.` chain is documented behaviour, not a bug: a non-nullable destination contract means "the caller has guaranteed every segment is populated." If they haven't, you get an NRE at the assignment site. Push that contract to the type system — make the destination nullable, or validate inputs upstream — when the guarantee can't be enforced.
:::

### Nullable destination → `?.`

```csharp
#nullable enable
public sealed record Address(string City);
public sealed record Customer(Address? Address);
public sealed record Src(Customer? Customer);
public sealed record Dst(string? City);

[Map<Src?, Dst?>]
public static partial class M
{
    [MapProperty("Customer.Address.City", "City")]
    public static partial Dst? Map(Src? src);
}

// Emits: City: src.Customer?.Address?.City
```

If any segment along the chain is null, the assignment lands `null` without throwing.

## Multi-Level Paths

The walk has no depth limit. `"A.B.C.D.E"` resolves the same way as `"A.B.C"` — segment by segment, halting on the first miss.

```csharp
public sealed record Settings(string Locale);
public sealed record Profile(Settings Settings);
public sealed record User(Profile Profile);
public sealed record Src(User User);
public sealed record Dst(string Locale);

[Map<Src, Dst>]
public static partial class M
{
    [MapProperty("User.Profile.Settings.Locale", "Locale")]
    public static partial Dst Map(Src src);
}
// Emits: Locale: src.User!.Profile!.Settings!.Locale
```

## Diagnostic ZAMP005 — Missing Segment

The walk halts at the first segment that doesn't resolve to a public property on the cursor type. The generator reports `ZAMP005` with the missing segment name and the cursor type as context.

```csharp
public sealed record Address(string City);
public sealed record Customer(Address Address);
public sealed record Src(Customer Customer);
public sealed record Dst(string City);

[Map<Src, Dst>]
public static partial class M
{
    [MapProperty("Customer.NoSuchProp.City", "City")]
    // ZAMP005: dotted path segment 'NoSuchProp' not found on Customer
    public static partial Dst Map(Src src);
}
```

Expect `ZAMP001` ("destination has no source") to follow on the same partial — the destination property `City` ended up unmapped because its source path failed to resolve. Fixing the path clears both.

## Inheritance Support

The walk uses an inheritance-aware property scan. Properties forwarded through a record's primary constructor (`record Cat(string Name) : Animal(Name)`) are reachable from the derived type even though they're declared on the base.

```csharp
public abstract record Animal(string Name);
public sealed record Cat(string Name, int Whiskers) : Animal(Name);

public sealed record Src(Cat Pet);
public sealed record Dst(string PetName);

[Map<Src, Dst>]
public static partial class M
{
    [MapProperty("Pet.Name", "PetName")]
    public static partial Dst Map(Src src);
}
// Emits: PetName: src.Pet!.Name
```

This matters for any record hierarchy that uses primary-constructor forwarding — the generator's `GetAllPublicProperties` walk climbs the base chain.

## Edge Cases

A few segment shapes the walk explicitly refuses to traverse:

- **Private property segment.** The walk only sees public properties. A private/internal segment in the middle of the path makes the walk halt as if the property didn't exist → `ZAMP005` plus a downstream `ZAMP001`.
- **Non-`INamedTypeSymbol` segment.** Tuple element accesses, `dynamic`, and a few exotic shapes don't carry the property metadata the walk needs. The walk halts at that segment with the same diagnostic pair.
- **Indexed access (`Items[0].Name`).** Not supported. Dotted paths are property-only — for collections see [Collections](collections.md), and project per-element via a sibling `[Map]` declaration on the element type.

## Where to Next

- **Mapping collections** — list/array/IEnumerable/IRO overloads and per-element nested mapping. See [Collections](collections.md).
- **Diagnostics catalogue** — `ZAMP005` and the rest of the codes with cause and fix. See [Diagnostics](diagnostics.md).
