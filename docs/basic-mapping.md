---
id: basic-mapping
title: Basic Mapping
description: Property matching, type conversions, and customisation attributes for compile-time mappers.
sidebar_position: 2
---

# Basic Mapping

`[Map<TSrc, TDst>]` on a `static partial class` is the entire surface for the common case. The generator inspects the destination's primary constructor, walks the source's public properties, and emits one `Map(TSrc) → TDst` method whose body is a single `new TDst(...)` expression. Everything on this page is what happens between those two anchors — how properties pair, how the generator picks a conversion, and the four attributes you reach for when the defaults aren't enough.

## By-Name Property Matching

Default matching is by exact, case-sensitive property name. No configuration, no convention layer — the destination constructor parameter name has to equal the source property name.

```csharp
public sealed record OrderRequest(int Id, string Notes);
public sealed record Order(int Id, string Notes);

[Map<OrderRequest, Order>]
public static partial class M { }
```

Generated body (single-element overload):

```csharp
public static global::Order Map(global::OrderRequest src)
{
    global::System.ArgumentNullException.ThrowIfNull(src);
    var __dst = new global::Order(
        Id: src.Id,
        Notes: src.Notes
    );
    return __dst;
}
```

:::tip
Need case-insensitive matching, or want unmatched destinations to fail the build instead of silently emitting `default`? See [Culture & Strict Mode](culture-and-strict.md) for `[CaseInsensitiveMapping]` and `[StrictDestinationMapping]`.
:::

## Conversion Paths

When the source and destination types for a paired property differ, the generator resolves a conversion. Five paths, tried in order. The first that fits wins; if none fit, the build fails with a `ZAMP00x` diagnostic.

| Source → Destination | Generator emits |
|---|---|
| `int → int` (identity) | `Id: src.Id` |
| `int → long` (implicit cast) | `X: src.X` |
| `int → OrderId` (single-arg ctor) | `Id: new OrderId(src.Id)` |
| `string → int` (`Parse`) | `Quantity: int.Parse(src.Quantity, CultureInfo.InvariantCulture)` |
| `string → MyEnum` (`Enum.Parse<T>`) | `C: Enum.Parse<Color>(src.C)` |

### 1. Identity

Same type both sides — direct assignment.

```csharp
public sealed record Src(int X);
public sealed record Dst(int X);
```

### 2. Implicit cast

C#'s built-in implicit numeric conversions (`int → long`, `float → double`, etc.). The generator emits the bare expression and lets the compiler insert the cast.

```csharp
public sealed record Src(int X);
public sealed record Dst(long X);
```

### 3. Single-argument constructor (value-object wrap)

If the destination is a type with a single-parameter constructor whose parameter type matches the source, the generator constructs it. This is how strong-ID wrapping works without ceremony.

```csharp
public readonly record struct OrderId(int Value);

public sealed record Src(int Id);
public sealed record Dst(OrderId Id);

[Map<Src, Dst>]
public static partial class M { }
// Emits: Id: new global::OrderId(src.Id)
```

### 4. `string → primitive` via `Parse`

`int`, `long`, `double`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset` and friends all expose a static `Parse(string, IFormatProvider)`. The generator calls it with `CultureInfo.InvariantCulture` by default.

```csharp
public sealed record Src(string Quantity);
public sealed record Dst(int Quantity);

[Map<Src, Dst>]
public static partial class M { }
// Emits: Quantity: int.Parse(src.Quantity, CultureInfo.InvariantCulture)
```

:::note
The default `InvariantCulture` is deliberate — locale-dependent parsing in mapping code is a class of bug nobody asks for. Override per-mapper or per-class with `[MappingCulture]` from [Culture & Strict Mode](culture-and-strict.md).
:::

### 5. `string → enum` via `Enum.Parse<T>`

```csharp
public enum Color { Red, Green, Blue }

public sealed record Src(string C);
public sealed record Dst(Color C);

[Map<Src, Dst>]
public static partial class M { }
// Emits: C: global::System.Enum.Parse<global::Color>(src.C)
```

The call is case-sensitive; pass an `IgnoreCase`-friendly source value or use `[MapValue]` to inject a parsed constant if you need different semantics.

## Customisation Attributes

Four attributes cover the common deviations from "match by name and convert". All are no-ops at runtime — they only affect what the generator emits at build time.

### `[MapProperty(sourceProperty, targetProperty)]` — rename

Renames a single property pair. Stack multiple instances to rename multiple pairs.

```csharp
public sealed record Src(string Foo);
public sealed record Dst(string Bar);

[Map<Src, Dst>]
public static partial class M
{
    [MapProperty("Foo", "Bar")]
    public static partial Dst Map(Src src);
}
```

Two things to note:

1. **Customisation requires a user-declared partial method.** The `public static partial Dst Map(Src src);` line tells the generator "this is the method I'm customising." Without it, the attribute has nowhere to attach. The generator implements the body.
2. **The same attribute is used for [Flattening](flattening.md)** — dotted source paths like `"Customer.Address.City"` walk nested properties. Same attribute, same shape.

### `[MapValue(targetProperty, value)]` — constant injection

Assigns a compile-time constant directly to a destination property. Useful for stamps, version markers, or any value that doesn't come from the source.

```csharp
public sealed record Src(int Id);
public sealed record Dst(int Id, string CreatedAt);

[Map<Src, Dst>]
public static partial class M
{
    [MapValue("CreatedAt", "2026-05-09T00:00:00Z")]
    public static partial Dst Map(Src src);
}
```

The constant has to be a valid C# attribute argument — primitives, strings, `typeof`, enum members. For runtime-computed values (e.g. `DateTimeOffset.UtcNow`), write the partial body yourself or wire it through a separate hook.

### `[MapperIgnoreSource]` — exclude a source property

Marks a source property as intentionally unmapped. Primarily used to suppress `ZAMP010` ("source property has no destination") under `[StrictSourceMapping]`. Without strict mode, the generator already ignores unmatched source properties silently; `[MapperIgnoreSource]` is the way to declare that silence is the intent.

```csharp
public sealed record Src(int Id, string InternalAuditField);
public sealed record Dst(int Id);

[Map<Src, Dst>]
[StrictSourceMapping]
public static partial class M { }

// Without [MapperIgnoreSource], ZAMP010 fires on InternalAuditField.
// To silence it intentionally:
public sealed record Src(int Id, [property: MapperIgnoreSource] string InternalAuditField);
```

See [Culture & Strict Mode](culture-and-strict.md) for the strict-mode pair.

### `[MapperIgnoreTarget]` — exclude a destination property

Marks a destination property the generator should skip during emission instead of failing with `ZAMP001` ("destination property has no source"). Useful when the destination type carries optional fields that aren't part of every mapping pair.

```csharp
public sealed record Src(int Id);
public sealed record Dst
{
    public Dst(int Id) { Id = Id; }
    public int Id { get; init; }
    [MapperIgnoreTarget]
    public string? Notes { get; init; }
}

[Map<Src, Dst>]
public static partial class M { }
```

:::warning v1 limitation
`[MapperIgnoreTarget]` is only effective on destination **properties**, not constructor parameters. If a constructor parameter has no matching source, the generator can't omit it from the `new TDst(...)` call. Work around it by lifting the field to an init-only property and ignoring it there, or by routing through a builder.
:::

## `[Obsolete]` Source / Destination — Silent Skip

The generator transparently filters `[Obsolete]`-marked source properties and destination constructor parameters from the matching pass. This is what lets you deprecate a field without immediately breaking every mapper that references it — the build keeps working as if the field weren't there.

```csharp
public sealed record Src(int Id, [property: Obsolete] string OldField);
public sealed record Dst(int Id);

[Map<Src, Dst>]
public static partial class M { }
// OldField is ignored, no diagnostic, no emission for it.
```

Explicit beats auto-skip: a `[MapProperty]` rename targeting an `[Obsolete]` source still resolves. The intent is "you tripped over this on purpose, we'll honour it."

```csharp
[MapProperty("OldField", "NewField")]
public static partial Dst Map(Src src);
// Walks OldField even though it's [Obsolete].
```

## Where to Next

- **Nested source paths** — flatten `Customer.Address.City` into a flat destination via [Flattening](flattening.md).
- **Mapping collections** — every `[Map]` automatically emits `List<T>`, `T[]`, `IEnumerable<T>`, and `IReadOnlyList<T>` overloads. See [Collections](collections.md).
- **Diagnostics catalogue** — every `ZAMPxxx` code with cause and fix. See [Diagnostics](diagnostics.md).
