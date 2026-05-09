---
id: diagnostics
title: Diagnostics
description: Compile-time diagnostics ZAMP001-ZAMP016 — every error and warning the generator can emit.
sidebar_position: 10
---

# Diagnostics

The generator emits sixteen distinct compile-time diagnostics. Errors fail the build; Warnings are advisory and surface in the IDE. All use the `ZAMP` prefix and the `ZeroAlloc.Mapping` category, so a `<NoWarn>` or `<WarningsAsErrors>` rule that targets `ZAMP*` covers every diagnostic the generator produces.

The source-of-truth for descriptors is `src/ZeroAlloc.Mapping.Generator/Diagnostics.cs`.

## Quick reference

| ID | Severity | Description |
|---|---|---|
| ZAMP001 | Error | Required destination property has no source |
| ZAMP002 | Error | No conversion path between source and destination property |
| ZAMP003 | Error | Ambiguous source property after `[MapProperty]` resolution |
| ZAMP004 | Error | `[Map]` chain references a `[TryMap]`-only mapper |
| ZAMP005 | Warning | `[MapProperty]` references a non-existent property name |
| ZAMP006 | Error | `[Map]`/`[TryMap]` applied to non-`static partial` class |
| ZAMP007 | Error | Nullable source mapped to non-nullable destination under `[Map]` |
| ZAMP008 | Error | Constructor selection is ambiguous |
| ZAMP009 | Error | `[ReverseMap]` is not safely reversible |
| ZAMP010 | Error | Source property is not consumed under strict source mapping |
| ZAMP011 | Error | Case-insensitive matching produces ambiguous source |
| ZAMP012 | Error | Destination type cannot be updated in place |
| ZAMP013 | Error | `[PolymorphicMap]` declared with no derived cases |
| ZAMP014 | Warning | `[PolymorphicMap]` over a sealed type is degenerate |
| ZAMP015 | Error | `[PolymorphicMap]` mixes `[Map]` and `[TryMap]` derived cases |
| ZAMP016 | Warning | Duplicate `[MappingCulture]` declarations |

## ZAMP001 — Required destination property has no source

**Severity**: Error.

**Trigger**: A required destination constructor parameter (or required property) has no matching source property by name, no `[MapProperty]` rename pointing at it, and no `[MapValue]` constant covering it.

**Triggering code** (from `DiagnosticTests.ZAMP001_DestinationHasNoSource_Reported`):

```csharp
public sealed record Src(int A);
public sealed record Dst(int A, int B);
[Map<Src, Dst>]
public static partial class M { }
```

**Fix**: Provide one of the three escape valves — rename, constant, or add a source property.

```csharp
[Map<Src, Dst>]
public static partial class M
{
    [MapValue("B", 0)]
    public static partial Dst Map(Src src);
}
```

## ZAMP002 — No conversion path between source and destination property

**Severity**: Error.

**Trigger**: Source and destination property types are unrelated — no implicit/explicit cast, no single-arg constructor, no `static Parse(string)` overload, and no nested `[Map<,>]`/`[TryMap<,>]` declared on the host class.

**Triggering code** (from `DiagnosticTests.ZAMP002_NoConversionPath_Reported`):

```csharp
public sealed class Foo { }
public sealed class Bar { }
public sealed record Src(Foo X);
public sealed record Dst(Bar X);
[Map<Src, Dst>]
public static partial class M { }
```

**Fix**: Declare a nested mapper for the inner pair, or expose a single-arg `Bar(Foo)` constructor on the destination type.

```csharp
[Map<Foo, Bar>]
[Map<Src, Dst>]
public static partial class M { }
```

## ZAMP003 — Ambiguous source property after `[MapProperty]` resolution

**Severity**: Error.

**Trigger**: After `[MapProperty]` rules apply, two source properties end up bound to the same destination parameter.

**Triggering code** (from `DiagnosticTests.ZAMP003_AmbiguousSource_Reported`):

```csharp
public sealed record Src(int X, int Other);
public sealed record Dst(int X);
[Map<Src, Dst>]
public static partial class M
{
    [MapProperty("Other", "X")]
    public static partial Dst Map(Src src);
}
```

Both `X` (auto-matched) and `Other` (renamed) target `Dst.X`.

**Fix**: Pick one source. Add `[MapperIgnoreSource]` to the unwanted property or remove the conflicting `[MapProperty]`.

```csharp
public sealed record Src(int X, [property: MapperIgnoreSource] int Other);
```

## ZAMP004 — `[Map]` chain references a `[TryMap]`-only mapper

**Severity**: Error.

**Trigger**: A total `[Map<Outer, Outer>]` references a nested mapper for the inner type that is only declared as `[TryMap<,>]`. The chain would have to swallow `Result<,>` failures silently — refused at compile time.

**Triggering code** (from `DiagnosticTests.ZAMP004_MapChainsTryMap_Reported`):

```csharp
public sealed record Inner1(int X);
public sealed record Inner2(int X);
public sealed record Outer1(Inner1 Child);
public sealed record Outer2(Inner2 Child);
[Map<Outer1, Outer2>]
[TryMap<Inner1, Inner2>]
public static partial class M { }
```

**Fix**: Add `[Map<Inner1, Inner2>]` alongside the `[TryMap<,>]`, or convert the outer to `[TryMap<,>]` so failure can propagate.

```csharp
[Map<Inner1, Inner2>]
[TryMap<Inner1, Inner2>]
[Map<Outer1, Outer2>]
public static partial class M { }
```

## ZAMP005 — `[MapProperty]` references a non-existent property name

**Severity**: Warning.

**Trigger**: A `[MapProperty(source, destination)]` rename names a destination property that does not exist on the destination type. The rename is silently dropped.

**Triggering code** (from `DiagnosticTests.ZAMP005_MapPropertyMissing_Reported`):

```csharp
public sealed record Src(int X);
public sealed record Dst(int X);
[Map<Src, Dst>]
public static partial class M
{
    [MapProperty("DoesNotExist", "X")]
    public static partial Dst Map(Src src);
}
```

**Fix**: Correct the typo or remove the stale `[MapProperty]`.

## ZAMP006 — `[Map]`/`[TryMap]` applied to non-`static partial` class

**Severity**: Error.

**Trigger**: The host class on which `[Map<,>]`/`[TryMap<,>]` is declared is not `static partial`. The generator emits into a partial part — both modifiers are required.

**Triggering code** (from `DiagnosticTests.ZAMP006_NotStaticPartialClass_Reported`):

```csharp
public sealed record Src(int X);
public sealed record Dst(int X);
[Map<Src, Dst>]
public class M { }
```

**Fix**: Make it `static partial`.

```csharp
[Map<Src, Dst>]
public static partial class M { }
```

## ZAMP007 — Nullable source mapped to non-nullable destination under `[Map]`

**Severity**: Error.

**Trigger**: A source property is nullable (`string?`, `int?`) but the destination expects a non-nullable counterpart, and the host uses `[Map<,>]` (total). A null at runtime would either NRE or silently coerce — refused at compile time.

**Triggering code** (from `DiagnosticTests.ZAMP007_NullableMismatch_Reported`):

```csharp
#nullable enable
public sealed record Src(string? Name);
public sealed record Dst(string Name);
[Map<Src, Dst>]
public static partial class M { }
```

**Fix**: Use `[TryMap<,>]` (so `null` becomes a `MappingError`), supply a `[MapValue]` fallback, or tighten the source type.

```csharp
[Map<Src, Dst>]
public static partial class M
{
    [MapValue("Name", "")]
    public static partial Dst Map(Src src);
}
```

## ZAMP008 — Constructor selection is ambiguous

**Severity**: Error.

**Trigger**: The destination type has multiple public constructors of equal preference (same parameter count, none clearly the largest non-copy ctor).

**Triggering code** (from `DiagnosticTests.ZAMP008_AmbiguousConstructor_Reported`):

```csharp
public sealed class Dst
{
    public Dst(int X, string Y) { }
    public Dst(int X, int Y) { }
}
public sealed record Src(int X, string Y);
[Map<Src, Dst>]
public static partial class M { }
```

**Fix**: Make one ctor non-public, or remove the duplicate. The generator's selection rule (largest non-copy public ctor) is documented in [Advanced](advanced.md).

## ZAMP009 — `[ReverseMap]` is not safely reversible

**Severity**: Error.

**Trigger**: A `[ReverseMap<,>]` host declares an asymmetric directive — `[MapProperty]`, `[MapValue]`, `[MapperIgnoreSource]`, etc. — that the generator cannot mechanically invert. Auto-reversal would lose information.

**Triggering code** (from `ReverseMapTests.ZAMP009_ReverseMap_With_MapProperty_Reported`):

```csharp
public sealed record Src(string Foo);
public sealed record Dst(string Bar);
[ReverseMap<Src, Dst>]
public static partial class M
{
    [MapProperty("Foo", "Bar")]
    public static partial Dst Map(Src src);
}
```

**Fix**: Replace `[ReverseMap<,>]` with two explicit `[Map<,>]`s and write each direction by hand. See [Reverse mapping](reverse-mapping.md).

```csharp
[Map<Src, Dst>]
[Map<Dst, Src>]
public static partial class M
{
    [MapProperty("Foo", "Bar")]
    public static partial Dst Map(Src src);
    [MapProperty("Bar", "Foo")]
    public static partial Src Map(Dst src);
}
```

## ZAMP010 — Source property is not consumed under strict source mapping

**Severity**: Error.

**Trigger**: The host carries `[StrictSourceMapping]` and a source property is neither consumed by a destination parameter nor opt-out via `[MapperIgnoreSource]`.

**Triggering code** (from `StrictSourceTests.ZAMP010_UnconsumedSourceProperty_Reported_UnderStrictMode`):

```csharp
public sealed record Src(int A, int B, int C);
public sealed record Dst(int A, int B);
[Map<Src, Dst>]
[StrictSourceMapping]
public static partial class M { }
```

**Fix**: Mark the unused source property with `[MapperIgnoreSource]`, drop it, or extend the destination. See [Culture & Strict Mode](culture-and-strict.md).

```csharp
public sealed record Src(int A, int B, [property: MapperIgnoreSource] int C);
```

## ZAMP011 — Case-insensitive matching produces ambiguous source

**Severity**: Error.

**Trigger**: `[CaseInsensitiveMapping]` is applied and two source properties collide on the same destination parameter once casing is folded.

**Triggering code** (from `CaseInsensitiveTests.ZAMP011_AmbiguousCaseInsensitiveMatch_Reported`):

```csharp
public sealed record Src(string Foo, string foo);
public sealed record Dst(string FOO);
[Map<Src, Dst>]
[CaseInsensitiveMapping]
public static partial class M { }
```

**Fix**: Disambiguate with `[MapProperty]` to pin one source explicitly, mark the loser `[MapperIgnoreSource]`, or `[Obsolete]` (which the generator skips by default — see [Advanced](advanced.md)).

```csharp
public sealed record Src(string Foo, [property: MapperIgnoreSource] string foo);
```

## ZAMP012 — Destination type cannot be updated in place

**Severity**: Error.

**Trigger**: An update-in-place overload (`partial void Map(TSrc src, TDst existingDst)`) is declared, but at least one mapped destination property has no public setter.

**Triggering code** (from `UpdateInPlaceTests.ZAMP012_Mixed_Settable_And_InitOnly_POCO_Reported`):

```csharp
public sealed record Src(int A, int B);
public sealed class Dst
{
    public int A { get; set; }
    public int B { get; init; }
}
[Map<Src, Dst>]
public static partial class M
{
    public static partial void Map(Src src, Dst existingDst);
}
```

**Fix**: Switch the offending property to `set`, drop the void overload (use the constructor form), or `[MapperIgnoreTarget]` the property. See [Update in place](update-in-place.md).

## ZAMP013 — `[PolymorphicMap]` declared with no derived cases

**Severity**: Error.

**Trigger**: A `[PolymorphicMap<TBase, TBaseDto>]` (or `[PolymorphicTryMap]`) is declared but no `[Map<TDerived, TDerivedDto>]` cases live alongside it. The dispatcher would always throw at runtime.

**Triggering code** (from `PolymorphicMapTests.ZAMP013_PolymorphicMap_With_No_Cases_Reported`):

```csharp
public abstract record Animal(string Name);
public abstract record AnimalDto(string Name);
[PolymorphicMap<Animal, AnimalDto>]
public static partial class M { }
```

**Fix**: Add at least one derived `[Map<,>]` case.

```csharp
public sealed record Cat(string Name) : Animal(Name);
public sealed record CatDto(string Name) : AnimalDto(Name);
[Map<Cat, CatDto>]
[PolymorphicMap<Animal, AnimalDto>]
public static partial class M { }
```

## ZAMP014 — `[PolymorphicMap]` over a sealed type is degenerate

**Severity**: Warning.

**Trigger**: The base type passed to `[PolymorphicMap<,>]` is `sealed`. Polymorphic dispatch over a single concrete pair is meaningless.

**Triggering code** (from `PolymorphicMapTests.ZAMP014_PolymorphicMap_Over_Sealed_Base_Reported`):

```csharp
public sealed record Cat(string Name);
public sealed record CatDto(string Name);
[Map<Cat, CatDto>]
[PolymorphicMap<Cat, CatDto>]
public static partial class M { }
```

**Fix**: Drop the `[PolymorphicMap<,>]` — `[Map<,>]` already does the job. The generator suppresses the duplicate dispatcher emission, so user code still compiles even if the warning is silenced.

## ZAMP015 — `[PolymorphicMap]` mixes `[Map]` and `[TryMap]` derived cases

**Severity**: Error.

**Trigger**: Derived cases under a `[PolymorphicMap<,>]` (or `[PolymorphicTryMap<,>]`) are inconsistent — some declared `[Map<,>]`, some `[TryMap<,>]`. The dispatcher's return type cannot reconcile both.

**Triggering code** (from `PolymorphicMapTests.ZAMP015_PolymorphicMap_Mixes_Map_And_TryMap_Cases_Reported`):

```csharp
[Map<Cat, CatDto>]
[TryMap<Dog, DogDto>]
[PolymorphicMap<Animal, AnimalDto>]
public static partial class M { }
```

**Fix**: Pick one kind for all derived cases. Note: declaring **both** `[Map<X, Y>]` *and* `[TryMap<X, Y>]` for the same pair is fine — that's coverage, not a mix.

```csharp
[Map<Cat, CatDto>]
[Map<Dog, DogDto>]
[PolymorphicMap<Animal, AnimalDto>]
public static partial class M { }
```

## ZAMP016 — Duplicate `[MappingCulture]` declarations

**Severity**: Warning.

**Trigger**: A class has `[MappingCulture]` applied to multiple partial parts. Only the first declaration is honoured; the rest are silently ignored.

**Triggering code** (from `DuplicateMappingCultureTests.ZAMP016_DuplicateMappingCulture_Reported`):

```csharp
public sealed record Src(string Quantity);
public sealed record Dst(int Quantity);
[Map<Src, Dst>]
[MappingCulture("nl-NL")]
public static partial class M { }
[MappingCulture("en-US")]
public static partial class M { }
```

**Fix**: Keep one `[MappingCulture]`, drop the duplicate.

## Where to next

- Performance: [Performance](performance.md).
- Testing diagnostics: [Testing](testing.md).
