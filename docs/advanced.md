---
id: advanced
title: Advanced
description: MappingError tree shape, Result integration, edge cases the generator handles.
sidebar_position: 12
---

# Advanced

## `MappingError` shape

`MappingError` is the structured failure type returned inside `Result<TDestination, MappingError>` from every `[TryMap<,>]`-generated body. It's a `readonly record struct` with four fields:

```csharp
public readonly record struct MappingError(
    string Code,
    string PropertyPath,
    string? Reason = null,
    IReadOnlyList<MappingError>? Children = null);
```

Field semantics:

- **`Code`** — open-ended structured string (e.g. `"mapping.constructor.threw"`, `"mapping.parse.failed"`). The generator emits a fixed set listed below; consumers are free to mint new codes when wrapping or transforming errors.
- **`PropertyPath`** — dotted/indexed path to the failing property: `"Customer.Email"`, `"Items[5].Quantity"`, or `"(root)"` when the source itself was null.
- **`Reason`** — optional human-readable detail. Typically captured from an inner `Exception.Message`. Safe to drop on a public surface.
- **`Children`** — optional nested errors. Used for collection aggregation (one entry per failing element) and for nested-mapper failures that surface multiple sub-errors. `null` for single-shot failures.

The struct is a value type, so a `default(MappingError)` is meaningful but uninformative — empty `Code` and `PropertyPath`. Generated code never produces a default-valued instance; it always sets `Code` and `PropertyPath`.

## Common error codes

The generator emits these codes; they're stable and safe to switch on.

| Code | Used by |
|---|---|
| `mapping.source.null` | `[TryMap<,>]` when the source argument is `null` |
| `mapping.constructor.threw` | `[TryMap<,>]` when the destination constructor throws (e.g. a smart-ctor validation failure) |
| `mapping.parse.failed` | `Parse(...)` conversion throws — invalid format, overflow, etc. |
| `mapping.collection.elements_failed` | `[TryMap<,>]` aggregate when at least one element of a collection mapping failed |
| `mapping.polymorphic.unhandled_type` | `[PolymorphicTryMap<,>]` runtime type has no declared `[TryMap<,>]` case |
| `mapping.hook.threw` | `[TryMap<,>]` when a `[BeforeMap]`/`[AfterMap]` hook throws |

User code is encouraged to mint additional codes when wrapping. A repository's `SaveAsync` failure might surface as `repo.save.conflict`; the generator's set is reserved to the `mapping.*` namespace.

## PropertyPath conventions

The generator builds paths mechanically:

- `(root)` — the source itself is the failure (typical for `mapping.source.null`).
- `Customer.Email` — single-level nested-mapper failure on the `Customer.Email` property.
- `Items[5].Quantity` — collection element index 5, on its `Quantity` property. Indices are 0-based.
- `Items[5].Customer.Email` — combined indexed and nested path.

When `Children` is populated (collection aggregate), each child carries the indexed path and the parent error's `PropertyPath` is `(root)` or the collection property's name.

## Result integration

`Result<T, MappingError>` comes from `ZeroAlloc.Results`. Generated `TryMap` methods return `Result<TDest, MappingError>`; the consumer threads success/failure through `Bind`, `Map`, `Match`:

```csharp
var result = AppMappings.TryMap(request)
    .Bind(order => repo.SaveAsync(order));

result.Match(
    onSuccess: saved => Console.WriteLine($"saved {saved.Id}"),
    onFailure: err   => Console.Error.WriteLine($"{err.Code} @ {err.PropertyPath}: {err.Reason}"));
```

`Bind` short-circuits on failure — once a `MappingError` appears, downstream steps don't run. There is no exception unwinding on the happy path.

For a primer on `Result<,>`, see [ZeroAlloc.Results](https://github.com/leon-roozekrans/ZeroAlloc.Results) (sister package).

## Edge cases the generator handles

These are corners that have explicit test coverage and bear documenting because they surprise people coming from reflection-based mappers.

### Multiple destination constructors

The generator picks the largest non-copy public constructor (records' synthesised `(TDst other)` copy ctor is filtered out). If two constructors tie, **ZAMP008** fires — see [Diagnostics](diagnostics.md). Make one ctor non-public to disambiguate.

### Inherited record properties

`PropertyMatcher.GetAllPublicProperties` walks the inheritance chain, so `record Cat(string Name) : Animal(Name)` exposes `Name` to the matcher even though it's syntactically only declared on the base. Primary-ctor parameters forwarded to a base record's primary ctor work as expected.

```csharp
public abstract record Animal(string Name);
public sealed record Cat(string Name) : Animal(Name);
public sealed record CatDto(string Name);
[Map<Cat, CatDto>] public static partial class M { }
```

### Records with positional + secondary constructors

When a record has its primary ctor *and* additional explicit constructors, the generator picks the one with the most parameters that isn't the synthetic copy ctor. Add an explicit `[MapProperty]` rename if the largest-ctor heuristic chooses the wrong one.

### `[Obsolete]` source/destination properties

Source properties marked `[Obsolete]` are silently filtered out of auto-matching — they don't fan out into `ZAMP010` complaints under `[StrictSourceMapping]` and don't collide under `[CaseInsensitiveMapping]` (`ZAMP011`). The same applies to obsolete destination parameters.

Explicit beats auto-skip: a `[MapProperty("ObsoleteSrc", "Dest")]` that names an `[Obsolete]` source still works. The generator treats the explicit rename as an opt-in to keep using the legacy name during a deprecation window.

### Nullable annotations honoured

Under `#nullable enable`, the generator reads source-property nullability and refuses to map `string?` → `string` under `[Map<,>]` (**ZAMP007**). Switch to `[TryMap<,>]` to surface a `MappingError` at runtime, or supply a `[MapValue]` fallback constant.

## Where to next

- Testing: [Testing](testing.md).
- Diagnostics: [Diagnostics](diagnostics.md).
