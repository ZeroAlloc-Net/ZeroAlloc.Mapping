---
id: polymorphic
title: Polymorphic Dispatch
description: Runtime-type switch dispatchers via [PolymorphicMap<,>] and [PolymorphicTryMap<,>].
sidebar_position: 6
---

# Polymorphic Dispatch

Inheritance hierarchies show up everywhere in domain models — `Animal`/`Cat`/`Dog`, `PaymentMethod`/`Card`/`BankTransfer`, event-sourced command unions. The natural mapping shape is "give me a `Map(Animal)` that returns the right `AnimalDto` subclass for whatever runtime type came in." That's `[PolymorphicMap<TBase, TBaseDestination>]`.

## The Pattern

Decorate the partial class with one `[Map<TDerived, TDerivedDto>]` per concrete leaf, plus a single `[PolymorphicMap<TBase, TBaseDto>]` that ties them together:

```csharp
public abstract record Animal(string Name);
public sealed record Cat(string Name, int Lives) : Animal(Name);
public sealed record Dog(string Name, string Breed) : Animal(Name);

public abstract record AnimalDto(string Name);
public sealed record CatDto(string Name, int Lives) : AnimalDto(Name);
public sealed record DogDto(string Name, string Breed) : AnimalDto(Name);

[Map<Cat, CatDto>]
[Map<Dog, DogDto>]
[PolymorphicMap<Animal, AnimalDto>]
public static partial class M { }
```

Source fixture: `PolymorphicMapTests.PolymorphicMap_Emits_Switch_Dispatcher`.

The base types can be `abstract`, the derived types should be `sealed`, and the derived types can either declare their own primary-ctor properties or forward through a base primary-ctor (`Cat(string Name, int Lives) : Animal(Name)`) — the generator's property-walk handles inherited primary-ctor parameters either way. Pick whichever matches the rest of your domain.

## Generated Dispatcher (Map)

For each per-derived `[Map]` you get the usual `Map(TDerived)` plus its four collection overloads. The `[PolymorphicMap]` adds one more method on top — the dispatcher:

```csharp
public static AnimalDto Map(Animal src)
{
    ArgumentNullException.ThrowIfNull(src);
    return src switch
    {
        Cat __0 => Map(__0),
        Dog __1 => Map(__1),
        _ => throw new InvalidOperationException(
            "No polymorphic mapping for runtime type " + src.GetType().FullName)
    };
}
```

The `switch` is an ordered chain of `is`-tests against the declared cases. An unmatched runtime type — for instance, a third subclass `Hamster : Animal` that you forgot to register — throws `InvalidOperationException` with the runtime type's full name in the message, so you can spot the missing case from a stack trace.

The dispatcher also gets the four collection overloads — `Map(List<Animal>)`, `Map(Animal[])`, `Map(IEnumerable<Animal>)`, `Map(IReadOnlyList<Animal>)` — each iterating elements through the dispatcher.

## `[PolymorphicTryMap]` Variant

Same shape, fallible. Use it when the per-derived mappings already use `[TryMap]` and you want to surface the polymorphic miss as an aggregate `MappingError` rather than throwing:

```csharp
[TryMap<Cat, CatDto>]
[TryMap<Dog, DogDto>]
[PolymorphicTryMap<Animal, AnimalDto>]
public static partial class M { }
```

Source fixture: `PolymorphicMapTests.PolymorphicTryMap_Emits_Result_Returning_Dispatcher`.

The emitted dispatcher uses an `if (src is …)` ladder rather than a `switch` expression. The reason is variance: `Result<TBaseDto, MappingError>` is invariant in its first type parameter, so a `Result<CatDto, _>` from `TryMap(Cat)` is not implicitly assignable to `Result<AnimalDto, _>`. The ladder unwraps each derived result and rewraps it explicitly:

```csharp
public static Result<AnimalDto, MappingError> TryMap(Animal src)
{
    if (src is null) return Result<AnimalDto, MappingError>.Failure(
        new MappingError("mapping.source.null", "(root)"));
    if (src is Cat __0)
    {
        var __r0 = TryMap(__0);
        return __r0.IsSuccess
            ? Result<AnimalDto, MappingError>.Success(__r0.Value)
            : Result<AnimalDto, MappingError>.Failure(__r0.Error);
    }
    if (src is Dog __1)
    {
        var __r1 = TryMap(__1);
        return __r1.IsSuccess
            ? Result<AnimalDto, MappingError>.Success(__r1.Value)
            : Result<AnimalDto, MappingError>.Failure(__r1.Error);
    }
    return Result<AnimalDto, MappingError>.Failure(new MappingError(
        "mapping.polymorphic.unhandled_type", "(root)",
        "runtime type " + src.GetType().FullName + " has no declared [TryMap]"));
}
```

The unmatched case becomes a structured `MappingError` with code `mapping.polymorphic.unhandled_type` — pattern-match on it in the caller alongside the other [Diagnostics](diagnostics.md) error codes.

## Diagnostics

Polymorphic dispatch carries three of its own validation rules.

### ZAMP013 (Error) — empty dispatcher

`[PolymorphicMap<Base, BaseDto>]` declared without any per-derived `[Map<,>]` cases on the same class. The dispatcher would always fall through to the throw clause, so the generator refuses:

```csharp
public abstract record Animal(string Name);
public abstract record AnimalDto(string Name);

[PolymorphicMap<Animal, AnimalDto>]   // ZAMP013: no derived cases declared.
public static partial class M { }
```

Fix: add at least one `[Map<TDerived, TDerivedDto>]`.

### ZAMP014 (Warning) — sealed base

The base type is `sealed`. Polymorphism over a closed type is degenerate — there are no possible derived runtime types beyond the declared one, so the dispatch is just a renamed direct call:

```csharp
public sealed record Cat(string Name);
public sealed record CatDto(string Name);

[Map<Cat, CatDto>]
[PolymorphicMap<Cat, CatDto>]   // ZAMP014: polymorphism over a sealed type is meaningless.
public static partial class M { }
```

This is a warning rather than an error so existing code keeps compiling, but the marker isn't earning its keep — drop it and rely on the per-decl `[Map]`.

### ZAMP015 (Error) — mixed kinds

Per-derived cases mix `[Map]` and `[TryMap]` for the polymorphic kind. Dispatching needs a single uniform return type — `TBaseDto` for `[PolymorphicMap]`, `Result<TBaseDto, MappingError>` for `[PolymorphicTryMap]` — and you can't ask one dispatcher to forward into both shapes.

```csharp
[Map<Cat, CatDto>]
[TryMap<Dog, DogDto>]
[PolymorphicMap<Animal, AnimalDto>]   // ZAMP015: mixed [Map] + [TryMap] derived cases.
public static partial class M { }
```

:::note
ZAMP015 only fires when a wrong-kind declaration has no matching-kind sibling for the same `(src, dst)` pair. Legitimately declaring **both** `[Map<Cat, CatDto>]` AND `[TryMap<Cat, CatDto>]` for the same pair is fine — the dispatcher picks the matching kind and ignores the other. The diagnostic is targeting the case where Dog has only a `[TryMap]` and the polymorphic kind is `[PolymorphicMap]` (so there's no infallible Dog-mapping for the dispatcher to call).
:::

## Degenerate-Pair Guard

A subtler case worth knowing about: if a per-decl `[Map<X, Y>]` already covers the polymorphic pair (e.g. `[Map<Cat, CatDto>]` next to `[PolymorphicMap<Cat, CatDto>]` on a sealed `Cat`), the dispatcher would emit a duplicate `Map(Cat) → CatDto` signature alongside the per-decl one, and the C# compiler would surface a duplicate-method error that masks the underlying ZAMP014 warning.

The generator detects this and **suppresses the polymorphic emission entirely** — the per-decl path produces the single `Map(Cat)` and its collection overloads, and ZAMP014 still fires to point you at the dead `[PolymorphicMap]` marker. Net effect: your code compiles cleanly even if you've suppressed the warning, and the diagnostic still surfaces the misuse.

Source fixture: `PolymorphicMapTests.Polymorphic_Over_Existing_Pair_Skips_Polymorphic_Emission`.

## Performance

The `switch` expression compiles to a chained `is`-test (the C# compiler picks a jump table when the case count and runtime layout cooperate, but the worst case is one type-test per case in declaration order). For the typical 2-4 derived types, this is a handful of `isinst` IL opcodes plus the call to the resolved per-derived `Map` — measurable, but well inside the standard 80 B/call budget documented in [Performance](performance.md). If you have a very wide hierarchy (10+ derived types) and the dispatcher is on a hot path, sort the `[Map<TDerived, …>]` declarations so the most common runtime types come first.

The `[PolymorphicTryMap]` ladder has the same shape minus the jump-table optimisation — it's a linear `if`-chain by construction. Cost is comparable, with one extra branch per case for the result-rewrap.

## Where to Next

- **Update-in-place** — apply a source onto an existing destination instead of allocating a fresh one. See [Update-in-Place](update-in-place.md).
- **Hooks** — `[BeforeMap]`/`[AfterMap]` for validation and post-mapping logic. See [Hooks](hooks.md).
