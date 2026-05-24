---
id: diagnostics
title: Diagnostics
description: Compile-time diagnostics ZAMP001-ZAMP021 — every error and warning the generator can emit.
sidebar_position: 10
---

# Diagnostics

The generator emits twenty-one distinct compile-time diagnostics. Errors fail the build; Warnings are advisory and surface in the IDE. All use the `ZAMP` prefix and the `ZeroAlloc.Mapping` category, so a `<NoWarn>` or `<WarningsAsErrors>` rule that targets `ZAMP*` covers every diagnostic the generator produces.

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
| ZAMP017 | Error | `[Map(Projection = true)]` uses a feature EF Core cannot translate |
| ZAMP018 | Error | `[Map(CycleSafe = true)]` references a non-CycleSafe nested mapping |
| ZAMP019 | Error | `[Map(DeepClone = true)]` reaches an uncloneable type |
| ZAMP020 | Error | `[Map(DeepClone = true)]` walks a cyclic type graph without `CycleSafe = true` |
| ZAMP021 | Error | `DeepClone + CycleSafe` reaches a primary-ctor-only type in a cycle |

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

## ZAMP017 — `[Map(Projection = true)]` uses a feature EF Core cannot translate

**Severity**: Error.

**Trigger**: A `[Map<,>(Projection = true)]` is declared on a class that also carries one of: `[BeforeMap]`/`[AfterMap]` hooks, `[MappingCulture]`, or `[PolymorphicMap<,>]`. The projection's emitted `Expression<Func<TSrc, TDst>>` cannot reference those features — EF Core's LINQ translator can't walk method calls, embedded culture parsing, or runtime-type switches. Also fires transitively when a nested mapping inlined into the projection violates the same constraint.

**Triggering code** (from `ProjectionDiagnosticTests.ZAMP017_FiresWhen_Projection_With_MappingCulture`):

```csharp
public sealed record Src(string Quantity);
public sealed record Dst(int Quantity);
[Map<Src, Dst>(Projection = true)]
[MappingCulture("nl-NL")]
public static partial class M { }
```

**Fix**: Move the projection-eligible mapping to a separate `static partial class` that doesn't carry the incompatible feature, or drop `Projection = true` if you don't need EF Core translation. See [IQueryable Projection](iqueryable-projection.md).

```csharp
[Map<Src, Dst>(Projection = true)]
public static partial class ReadMappings { }

[Map<Src, Dst>]
[MappingCulture("nl-NL")]
public static partial class WriteMappings { }
```

## ZAMP018 — `[Map(CycleSafe = true)]` references a non-CycleSafe nested mapping

**Severity**: Error.

**Trigger**: A `[Map<,>(CycleSafe = true)]` references a nested `[Map<,>]` (on the same class) that is not also declared `CycleSafe = true`. The cycle-safe recursion would lose the tracker on the nested call, and a back-reference through the nested type would blow the stack at runtime.

**Triggering code** (from `CycleSafeDiagnosticTests.ZAMP018_FiresWhen_Nested_NotCycleSafe`):

```csharp
public sealed class Customer { public List<Order> Orders { get; set; } = new(); }
public sealed class Order { public Customer Customer { get; set; } = null!; }
public sealed class CustomerDto { public List<OrderDto> Orders { get; set; } = new(); }
public sealed class OrderDto { public CustomerDto Customer { get; set; } = null!; }
[Map<Customer, CustomerDto>(CycleSafe = true)]
[Map<Order, OrderDto>]
public static partial class M { }
```

**Fix**: Mark the nested mapping `CycleSafe = true` too. The check is transitive — every mapping reachable from a `CycleSafe = true` declaration must also be `CycleSafe = true`. See [Cycle-Safe Mapping](cycle-safe-mapping.md).

```csharp
[Map<Customer, CustomerDto>(CycleSafe = true)]
[Map<Order, OrderDto>(CycleSafe = true)]
public static partial class M { }
```

## ZAMP019 — `[Map(DeepClone = true)]` reaches an uncloneable type

**Severity**: Error.

**Trigger**: The deep-clone walker reaches a type that has no public parameterless constructor and no fully-covering constructor for its mapped properties — typically an abstract class, a type whose only constructor takes parameters the walker cannot bind, or a type with init-only properties not covered by a constructor parameter.

**Triggering code** (from `DeepCloneDiagnosticTests.ZAMP019_FiresWhen_Reaches_AbstractType`):

```csharp
public abstract class AbstractBase { public int Id { get; set; } }
public sealed class Holder { public AbstractBase Inner { get; set; } = null!; }
[Map<Holder, Holder>(DeepClone = true)]
public static partial class M { }
```

**Fix**: Declare an explicit nested `[Map<AbstractBase, AbstractBase>]` (e.g. dispatched via `[PolymorphicMap]`) that takes over the walk, or change the shape of the uncloneable type so it has a parameterless ctor and settable properties. See [Deep Clone](deep-clone.md).

```csharp
public sealed class ConcreteImpl : AbstractBase { }
[Map<ConcreteImpl, ConcreteImpl>]
[PolymorphicMap<AbstractBase, AbstractBase>]
[Map<Holder, Holder>(DeepClone = true)]
public static partial class M { }
```

## ZAMP020 — `[Map(DeepClone = true)]` walks a cyclic type graph without `CycleSafe = true`

**Severity**: Error.

**Trigger**: The deep-clone walker hits a cycle in the type graph (e.g. `Customer` references `Order`s, each `Order` references `Customer`) and the declaration is not also `CycleSafe = true`. Without runtime tracking, the emitted code would recurse infinitely on any instance graph that exercises the cycle.

**Triggering code** (from `DeepCloneDiagnosticTests.ZAMP020_FiresWhen_TypeGraph_Cycles_Without_CycleSafe`):

```csharp
public sealed class Customer { public List<Order> Orders { get; set; } = new(); }
public sealed class Order { public Customer Customer { get; set; } = null!; }
[Map<Customer, Customer>(DeepClone = true)]
public static partial class M { }
```

**Fix**: Compose `DeepClone = true` with `CycleSafe = true`. The walker emits literal `new T { ... }` clones and the cycle-safe machinery threads the runtime tracker through them. See [Deep Clone](deep-clone.md#composing-with-cyclesafe--zamp020) and [Cycle-Safe Mapping](cycle-safe-mapping.md).

```csharp
[Map<Customer, Customer>(DeepClone = true, CycleSafe = true)]
public static partial class M { }
```

## ZAMP021 — `DeepClone + CycleSafe` reaches a primary-ctor-only type in a cycle

**Severity**: Error.

**Message**: `[Map<Src, Dst>(DeepClone = true, CycleSafe = true)] cycles through type 'TypeX' which has no public parameterless constructor — add a parameterless ctor on 'TypeX', declare an explicit nested [Map<,>] for it, or drop CycleSafe = true`.

**Trigger**: The generator walks the reachable type graph from a `[Map(DeepClone = true, CycleSafe = true)]` declaration's destination and discovers a cycle that passes through a type with no public parameterless constructor (typically a `record` or immutable class). CycleSafe requires `tracker.Add(src, __new)` *before* walking children so a back-reference resolves to the in-flight clone — atomic primary-ctor construction can't satisfy that ordering.

**Triggering code** (from `CycleSafeDeepCloneDiagnosticTests.ZAMP021_FiresWhen_Cycle_Through_PrimaryCtor_Only_Type`):

```csharp
public sealed record Box(string Label, Box? Inner);
[Map<Box, Box>(DeepClone = true, CycleSafe = true)]
public static partial class M { }
```

**Fix**:

- Add a public parameterless ctor on the cyclic type — works for records via `public Box() : this("", null) { }`.
- Declare an explicit nested `[Map<TypeX, TypeX>]` so the generator emits a `Map(src, tracker)` overload it can call.
- Drop `CycleSafe = true` (loses cycle protection; falls back to `DeepClone` solo behaviour with `ZAMP020` enforcing acyclicity).

See [Cycle-Safe Mapping → Combining with `DeepClone`](cycle-safe-mapping.md#combining-with-deepclone).

```csharp
public sealed record Box
{
    public string Label { get; init; } = "";
    public Box? Inner { get; init; }
    public Box() { }
}
[Map<Box, Box>(DeepClone = true, CycleSafe = true)]
public static partial class M { }
```

## Where to next

- Performance: [Performance](performance.md).
- Testing diagnostics: [Testing](testing.md).
- Projection feature reference: [IQueryable Projection](iqueryable-projection.md).
- Cycle-safe feature reference: [Cycle-Safe Mapping](cycle-safe-mapping.md).
- Deep-clone feature reference: [Deep Clone](deep-clone.md).
