# ZeroAlloc.Mapping — v1 extensions design

**Date:** 2026-05-07
**Scope:** Six features promoted from `docs/backlog.md` and added to PR [#1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/pull/1) before v1 ships. All additive — no breaking changes to the v1 surface delivered by `2026-05-07-mapping-design.md`.

## Items

| ID | Title | Surface |
|---|---|---|
| B1 | Flattening | Extend `[MapProperty]` to accept dotted source paths |
| B4 | `[BeforeMap]` / `[AfterMap]` hooks | Two new method-target attributes |
| B10 | Skip `[Obsolete]` members | Silent — no new surface |
| B12 | `[ReverseMap]` | New class-level attribute |
| B13 | Case-insensitive matching | New class-level marker `[CaseInsensitiveMapping]` |
| B14 | Strict source mode | New class-level marker `[StrictSourceMapping]` + ZAMP010 |

## Goals

- **Each feature opt-in.** Existing v1 behavior unchanged for users who don't touch the new attributes.
- **No new runtime allocations.** All extensions resolve at generation time; emitted code remains zero-alloc on the happy path.
- **Reuse the existing diagnostic + emission machinery.** No new emitters, no new model layers — extend what Tasks 11-17 already shipped.
- **Each feature ships behind its own snapshot tests.** No item piggy-backs on another's tests.

## Decisions

### 1. B1 — Flattening: explicit dotted paths

`[MapProperty(sourceProperty: string, targetProperty: string)]` already targets `AttributeTargets.Method` and lives on the user-declared partial. Currently the source name is treated as a flat property lookup. Extension: if the string contains `.`, split on dots and walk the source object. The walk emits `src.Customer?.Address?.City` (null-conditional through the chain) under `[Map]`, or a try/catch-trapped expression under `[TryMap]`.

**Why explicit, not implicit name-concat (Mapperly-style):** auto-magic would change emission silently when a user adds a property to a source DTO. Explicit dotted paths are unambiguous, refactor-safe (`nameof` doesn't help across dots, but the compile-time string lookup catches typos via ZAMP005), and require zero new attribute surface.

**Diagnostic surface:** ZAMP005 already fires for missing `[MapProperty]` source/target names. Extension: ZAMP005 also fires when any segment of a dotted path is missing.

**Null handling:**
- Under `[Map]` with a non-nullable destination — generator emits `src.A!.B!.C` (forces non-null assertion). NRE at runtime if a segment is null. Documented; ZAMP007-style nullable check covers the destination side.
- Under `[Map<…?, …?>]` with nullable destination — `src.A?.B?.C` (null-conditional). Result is naturally `null`-safe.
- Under `[TryMap]` — wrapped by the existing try/catch; an NRE becomes `MappingError("mapping.flattening.null_segment", "<dotted.path>")`.

### 2. B4 — `[BeforeMap]` / `[AfterMap]` hooks

Two new method-target attributes:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class BeforeMapAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class AfterMapAttribute : Attribute { }
```

User declares hook methods on the same `static partial class`:

```csharp
[Map<OrderRequest, Order>]
public static partial class M
{
    [BeforeMap]
    public static void Validate(OrderRequest src) { /* ... */ }

    [AfterMap]
    public static void Audit(OrderRequest src, Order dst) { /* ... */ }
}
```

**Discovery:** `MapperDiscovery` enumerates static methods on the host class with `[BeforeMap]` or `[AfterMap]`. Signature contract:
- `BeforeMap`: takes one parameter assignable from `TSource`. Returns `void`.
- `AfterMap`: takes two parameters — `TSource` first, `TDestination` second. Returns `void`.

Hooks fire on **every** mapping declared on the class whose `TSource`/`TDestination` are signature-compatible. Multiple hooks per kind run in declaration order.

**Generator emission:** before/after the existing `new TDst(...)` block:

```csharp
public static Order Map(OrderRequest src)
{
    ArgumentNullException.ThrowIfNull(src);
    Validate(src);                                  // [BeforeMap] inline
    var __dst = new Order(Id: src.Id, …);
    Audit(src, __dst);                              // [AfterMap] inline
    return __dst;
}
```

Under `[TryMap]` the hooks live inside the existing try-block. Hook exceptions surface as `MappingError("mapping.hook.threw", "(root)", ex.Message)`.

**Allocation budget:** static method calls compile to direct invocations — zero alloc beyond what the user's hook itself does. The deny-path budget already covers exception cost.

### 3. B10 — Silent `[Obsolete]` skip

`PropertyMatcher` filters out source properties and constructor parameters whose owning symbol carries `[ObsoleteAttribute]`. ZAMP001 does not fire for an `[Obsolete]` destination param (because the generator behaves as if the user wrote `[MapperIgnoreTarget]`).

**Edge case:** if a user *wants* to map an obsolete property explicitly via `[MapProperty]`, that wins — explicit beats auto-skip. Generator emits a warning (`#pragma warning disable CS0612`) around the touch site.

**No new attribute, no new diagnostic. Trivial change to PropertyMatcher.**

### 4. B12 — `[ReverseMap<TSrc, TDst>]`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ReverseMapAttribute<TSource, TDestination> : Attribute { }
```

`MapperDiscovery` desugars `[ReverseMap<A, B>]` into two `MappingDecl` entries — one `(A → B, Map)` and one `(B → A, Map)`. The `[TryMap]`-equivalent is `[ReverseTryMap<,>]` — same desugar but `Kind = TryMap` for both.

**Safety net — ZAMP009:** if the user-declared partial method for either direction carries `[MapProperty]`, `[MapValue]`, or `[MapperIgnoreTarget]`, those aren't safely reversible (information-asymmetric). Generator emits ZAMP009 (Error) on the `[ReverseMap]` declaration with the offending attribute named, telling the user to write two explicit `[Map<,>]`s instead. This is a hard guard — the safer "split into two declarations" path is always one error away.

**Property matching:** by-name in both directions. If `A` has property `X` and `B` has property `Y` declared via `[MapProperty]` rename, the rename only flows in one direction; ZAMP009 fires.

### 5. B13 — `[CaseInsensitiveMapping]` marker

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CaseInsensitiveMappingAttribute : Attribute { }
```

When present on the static partial class, `PropertyMatcher` switches its source-property dictionary to `StringComparer.OrdinalIgnoreCase`. Affects every `[Map]`/`[TryMap]` declaration on that class.

**Ambiguity guard — ZAMP011:** if case-insensitive matching produces two source properties matching the same destination param (e.g. `src.Foo` and `src.foo` both hitting `dst.Foo`), generator emits ZAMP011 (Error). Forces the user to disambiguate via `[MapperIgnoreSource]` or `[MapProperty]`.

### 6. B14 — `[StrictSourceMapping]` marker

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StrictSourceMappingAttribute : Attribute { }
```

When present, the generator emits **ZAMP010 (Error)** for every source property not consumed by any destination param and not marked `[MapperIgnoreSource]`. Without this attribute, unmatched source properties remain silent (today's v1 behavior).

**Why error not warning:** strict mode is opt-in. A user opting in is asking for "don't let me silently drop fields"; a warning is too easy to miss. They can always remove the attribute.

## Diagnostics added

| ID | Severity | Trigger |
|---|---|---|
| **ZAMP009** | Error | `[ReverseMap<,>]` on a class whose partial method has `[MapProperty]` / `[MapValue]` / `[MapperIgnoreTarget]` (information-asymmetric — not safely reversible) |
| **ZAMP010** | Error | Under `[StrictSourceMapping]`, a source property is unmatched and not `[MapperIgnoreSource]`-tagged |
| **ZAMP011** | Error | Under `[CaseInsensitiveMapping]`, two source properties collide on the same destination param |

ZAMP005 (existing) extends to validate every segment of a dotted `[MapProperty]` source path.

## File-level changes

```
src/ZeroAlloc.Mapping/Attributes/
├── BeforeMapAttribute.cs            (new)
├── AfterMapAttribute.cs             (new)
├── ReverseMapAttribute.cs           (new — generic <TSrc, TDst>)
├── CaseInsensitiveMappingAttribute.cs   (new)
├── StrictSourceMappingAttribute.cs  (new)

src/ZeroAlloc.Mapping.Generator/
├── PropertyMatcher.cs               (extend: dotted paths, case-insensitive, [Obsolete] skip)
├── MapperDiscovery.cs               (extend: desugar [ReverseMap], collect [Before/After] hooks)
├── MapperModel.cs                   (extend: MapperClass.Hooks, MapperClass.CaseInsensitive, …)
├── MapEmitter.cs                    (extend: emit hook calls, dotted-path expressions)
├── TryMapEmitter.cs                 (extend: same)
├── Diagnostics.cs                   (add ZAMP009/010/011)
├── MappingGenerator.cs              (wire new diagnostics)

tests/ZeroAlloc.Mapping.Tests/
├── Attributes/AttributeTests.cs     (extend: 5 new attribute-target tests)

tests/ZeroAlloc.Mapping.Generator.Tests/
├── FlatteningTests.cs               (new — 4 snapshot tests)
├── HooksTests.cs                    (new — 3 snapshot tests)
├── ReverseMapTests.cs               (new — 2 snapshot tests + ZAMP009)
├── CaseInsensitiveTests.cs          (new — 2 snapshot tests + ZAMP011)
├── StrictSourceTests.cs             (new — 1 snapshot test + ZAMP010)
├── ObsoleteSkipTests.cs             (new — 2 snapshot tests)
```

Existing 14 emission snapshots and 8 ZAMP diagnostic tests remain unchanged.

## Out of scope (still deferred)

- B2 Polymorphic mapping
- B3 IQueryable projections
- B5 Update-in-place
- B6 Cycle detection
- B7 Private members
- B8 Open-generic mappings
- B9 FormatProvider customization
- B11 Deep cloning

`docs/backlog.md` updated to remove B1, B4, B10, B12, B13, B14 from the deferred list and renumber the survivors.

## Validation strategy

1. **Snapshot tests** — every new feature lands with at least one Verify-driven snapshot of the generated `.g.cs`.
2. **Diagnostic tests** — ZAMP009/010/011 each get a positive trigger test plus the existing `Clean_Source_Emits_No_Diagnostics` regression check.
3. **Allocation budgets** — extend `tests/ZeroAlloc.Mapping.Tests/AllocationBudgetTests.cs` with one new budget per feature that allocates non-trivially (hooks: 80 B/call; reverse: same as forward; flatten: same as flat). Skip budgets for features that emit identical code shapes to v1 (case-insensitive, strict, obsolete).
4. **AOT smoke** — extend `samples/ZeroAlloc.Mapping.AotSmoke/Program.cs` to exercise one declaration of each feature; CI's `aot-smoke` job verifies AOT-publish-ability.

## Test counts after implementation

| Project | v1 | v1+ext | Δ |
|---|--:|--:|--:|
| `ZeroAlloc.Mapping.Tests` | 17 | ~22 | +5 |
| `ZeroAlloc.Mapping.Generator.Tests` | 23 | ~38 | +15 |

## Implementation order

1. **B10 first** (1h, smallest blast radius — proves the PropertyMatcher extension point).
2. **B13 second** (½d, same matcher extension point, builds on B10's filter pattern).
3. **B14 third** (½d, pure diagnostic — no emission changes).
4. **B1 fourth** (1d, dotted-path emission — biggest emitter change).
5. **B4 fifth** (½d, hook discovery + inline call emission).
6. **B12 last** (½d, builds on everything — desugaring needs all the matcher logic in place; ZAMP009 needs to know what's "asymmetric").

Total estimate: ~2½ days of work. PR #1 grows by 6 features without breaking existing snapshots.
