---
id: getting-started
title: Getting Started
slug: /
description: Install ZeroAlloc.Mapping and write your first source-generated mapper in five minutes.
sidebar_position: 1
---

# Getting Started

ZeroAlloc.Mapping is a Roslyn source-generator-driven object mapper for .NET 8, 9, and 10. It emits strongly-typed mapping methods at compile time, so the success path runs with zero heap allocation beyond the destination instance itself and is fully native-AOT compatible. Fallible mappings integrate with [`ZeroAlloc.Results`](https://github.com/Roozekrans/ZeroAlloc.Results) so smart-constructor failures surface as a structured `Result<T, MappingError>` instead of a thrown exception.

## Installation

```bash
dotnet add package ZeroAlloc.Mapping
```

The generator runs automatically as part of `dotnet build`. There is no DI registration, no startup wire-up, and no runtime reflection — every dispatch you call was emitted into your assembly during compilation.

## Your First Mapping

Three steps from a clean project to a working source-generated mapper.

### Step 1 — Define source and destination types

Records are the family idiom; plain classes work just as well. The generator only cares that the destination has a constructor (or settable properties) the emitter can reach.

```csharp
public sealed record OrderRequest(int Id, string Notes);
public sealed record Order(int Id, string Notes);
```

### Step 2 — Declare the mapper

Apply `[Map<TSrc, TDst>]` to a `static partial class`. The generator fills in the partial.

```csharp
using ZeroAlloc.Mapping;

[Map<OrderRequest, Order>]
public static partial class AppMappings { }
```

### Step 3 — Use the generated method

```csharp
var order = AppMappings.Map(new OrderRequest(42, "rush"));
// order.Id == 42, order.Notes == "rush"
```

That's the full happy-path loop. Every other feature in this doc set is a refinement on top of these three steps.

## What Got Generated?

Build the project and the generator drops something close to this into your assembly:

```csharp
partial class AppMappings
{
    public static global::Order Map(global::OrderRequest src)
    {
        global::System.ArgumentNullException.ThrowIfNull(src);
        var __dst = new global::Order(
            Id: src.Id,
            Notes: src.Notes
        );
        return __dst;
    }

    // ... + 4 collection overloads (List<T>, T[], IEnumerable<T>, IReadOnlyList<T>)
}
```

A single direct constructor call, no intermediate buffers, no boxing. Property names line up by case-sensitive match; mismatches raise a compile-time diagnostic rather than silently dropping data.

:::tip
Don't want the four auto-emitted collection overloads on a particular mapper? Add `[SkipCollectionOverloads]` next to `[Map<,>]`. See [Collections](collections.md) for the full story.
:::

## Fallible Mapping with `[TryMap]`

When destination construction can fail — typically because a property is a smart-constructor value object that throws on invalid input — use `[TryMap<TSrc, TDst>]` instead. The generator emits a method that returns `Result<TDst, MappingError>` from `ZeroAlloc.Results` and converts the throwing constructor into a structured failure.

```csharp
public sealed record SignUpRequest(string Email);
public sealed record User(Email Email);

public readonly record struct Email
{
    public Email(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("empty", nameof(value));
        Value = value;
    }
    public string Value { get; }
}

[TryMap<SignUpRequest, User>]
public static partial class AuthMappings { }
```

Both paths return the same `Result` shape:

```csharp
// Success path
var success = AuthMappings.TryMap(new SignUpRequest("user@example.com"));
// success.IsSuccess == true
// success.Value.Email.Value == "user@example.com"

// Failure path — empty email triggers ArgumentException in Email's smart ctor
var failure = AuthMappings.TryMap(new SignUpRequest(""));
// failure.IsSuccess == false
// failure.Error.Code == "mapping.constructor.threw"
// failure.Error.PropertyPath == "(root)"
```

The error carries a stable `Code`, the `PropertyPath` where the failure occurred, and the inner exception. See [Advanced](advanced.md) for the full `MappingError` tree (collection-element failures carry indexed paths like `Items[3].Email`, nested constructors carry dotted paths, etc.) and for tips on bridging into application-level result pipelines.

## Auto-Generated Collection Overloads

Every `[Map<,>]` automatically emits four collection overloads alongside the scalar one, so you rarely need to write a `Select(...).ToList()` by hand:

```csharp
List<Order> orders = AppMappings.Map(new List<OrderRequest> { new(1, "a"), new(2, "b") });
Order[] arr      = AppMappings.Map(new[] { new OrderRequest(3, "c") });
```

`IEnumerable<T>` and `IReadOnlyList<T>` overloads round out the set. See [Collections](collections.md) for nested-element rules, capacity hints, and EF Core integration.

## Where to Next

- **Customisation** — property renames, ignores, conversions, and custom expressions: [Basic Mapping](basic-mapping.md).
- **Diagnostics** — every emission rule is enforced via `ZAMP001`–`ZAMP016`. Each code has a fix-it example: [Diagnostics](diagnostics.md).
- **Cookbook** — start with the canonical CQRS recipe: [Command → Domain](cookbook/01-command-to-domain.md).
