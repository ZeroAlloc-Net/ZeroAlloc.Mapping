# ZeroAlloc.Mapping — Design

**Date:** 2026-05-07
**Status:** Designed, ready for implementation plan
**Origin:** Workspace-level `docs/BACKLOG.md` — "the missing link between commands, domain objects, and DTOs in the ZeroAlloc stack." Selected today as the highest-value pick across the org-wide and per-repo backlogs (above Authorization #5, Fluxor, etc.). Unblocks the `za-clean` Clean-Architecture template.

## Problem

Every Web API and every Clean-architecture or vertical-slice project written in C# needs Command → Domain → DTO mapping. The existing tools — AutoMapper (reflection-driven), Mapperly (source-generated, mature) — do not integrate with the ZeroAlloc family's first-class types:

- **`ZeroAlloc.Results`** — failures should propagate as `Result<T, E>` values, not exceptions on the hot path. Mapperly throws.
- **`ZeroAlloc.ValueObjects`** smart constructors — Mapperly calls them but treats the throwing behavior as opaque; users get `ArgumentException` and have to figure out which property failed.
- **`ZeroAlloc.Serialisation`** awareness — DTOs annotated `[ZeroAllocSerializable]` are first-class in the family; mappers should know about them at compile time.

`ZeroAlloc.Mapping` is the family-idiomatic mapper. It is **not a Mapperly replacement** — it deliberately ships a smaller v1 surface focused on the Clean-arch / vertical-slice case, with a clear deferred-items backlog for features users might ask for later.

## Scope

A new repo `ZeroAlloc.Mapping` and one initial NuGet package (`ZeroAlloc.Mapping`) bundling both the runtime types and the Roslyn generator into a single install (per the family's "bundle the generator" pattern from `Cache` and `Specification`).

Targets `net8.0`, `net9.0`, `net10.0` — same TFM matrix as the rest of the family. `<IsAotCompatible>true</IsAotCompatible>` from day one.

## Architecture

Two layers, mirroring the family pattern:

1. **Runtime (~80 LOC, hand-coded)** in `src/ZeroAlloc.Mapping/`:
   - Generic `[Map<TSource, TDestination>]` and `[TryMap<TSource, TDestination>]` attributes (class-level, allow multiple).
   - `MappingError` `readonly record struct`.
   - Per-method attributes: `[MapProperty]`, `[MapperIgnoreSource]`, `[MapperIgnoreTarget]`, `[MapValue]`.
   - No reflection at runtime.

2. **Generator** (Roslyn `IIncrementalGenerator`, `netstandard2.0`) in `src/ZeroAlloc.Mapping.Generator/`:
   - Discovers `[Map<,>]` / `[TryMap<,>]`-decorated `static partial class`es.
   - For each (TSource, TDestination): walks destination's required+optional properties, matches sources by name (or via `[MapProperty]` rename / `[MapValue]` constant), picks a conversion path, emits the static map method.
   - Emits one `.g.cs` per (Class, Source, Destination, Kind) triple into the user's compilation.
   - Bundled into the runtime NuGet via `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` + `IncludeGeneratorInPackage` MSBuild target (precedent from `Cache` / `Specification`).

Three external dependencies:
- `ZeroAlloc.Results` — `Result<T, E>` for the `[TryMap]` path.
- *(generator-only)* `Microsoft.CodeAnalysis.CSharp` 4.14+.
- *(no `Mapperly` reference, no `AutoMapper`, no `Microsoft.Extensions.*`)*.

The generator emits to the user's compilation per `static partial class`. **Does NOT** emit DI registrations — mappers are pure static methods. (User wraps in a service themselves if injection is wanted.)

## Foundational design decisions

Each was an explicit branch point during brainstorming. Recording rationale so future maintainers don't re-litigate.

### 1. Declaration shape: `[Map<,>]` / `[TryMap<,>]` on `static partial class`

**Decision:** generic class-level attributes on a host `static partial class`. Generator emits one static method per declared mapping into the same partial class.

**Rejected:** `[Map]` on the source type (annotation-driven) — viral, every domain type gets opinionated about every DTO it could map to. Central registry attribute on a marker class — too implicit about which methods exist.

**Why:** matches Mapperly's most-used shape; generated code lives where users expect it (alongside the partial class declaration); zero runtime types to instantiate.

### 2. v1 feature scope

**Must-have:**
1. `[Map<TSrc, TDst>]` and `[TryMap<TSrc, TDst>]` declaration on `static partial class`.
2. Auto property-by-name matching (case-sensitive in v1) with type-equal direct assignment.
3. Type conversions (BCL set): identity copy, implicit/explicit casts, single-arg constructors (covers `[ValueObject]` smart constructors), `Parse(string)` (with `CultureInfo.InvariantCulture`), static factory methods (`Create`, `FromX`).
4. `[MapProperty(src, dst)]` — rename.
5. `[MapperIgnoreSource]` / `[MapperIgnoreTarget]`.
6. Collection mapping — `IEnumerable<TSrc> → IEnumerable<TDst>` (or `List<T>` / `T[]` / `IReadOnlyList<T>`) by element, when an element-level mapper exists.
7. Nested object mapping — when destination property has a `[Map]` / `[TryMap]` already declared in the same compilation, generator chains.
8. Diagnostics ZAMP001..ZAMP008 (full table below).
9. Enum-to-enum by name (ByValue is just a cast — already covered by #3).
10. `[MapValue(targetProperty, constant)]` — constant injection (audit fields, fixed values).
11. Day-one AOT smoke + `AllocationGate` on the happy paths (precedent from `Authorization` PR #11 + `Mediator.Authorization`).

**Deferred to v2+** — all captured in §"Out of scope" below and seeded into `docs/backlog.md` at scaffold time.

### 3. Failure model: split — `[Map]` vs `[TryMap]` (option C)

**Decision:** explicit user choice per mapping.

- `[Map<A, B>]` → method returns `B`. Throws on inner failure (smart-ctor exception, `Parse` exception, etc.). No try/catch wrapping. Zero overhead for the 80% of mappings that are infallible.
- `[TryMap<A, B>]` → method returns `Result<B, MappingError>`. Wraps each fallible step in try/catch with structured `MappingError` accumulation. Pays try/catch cost on the failure path.

**Rejected:**
- **A. Always-Result** — 80% of mappings pay Result-wrapping cost they don't need.
- **B. Always-direct (Mapperly-style)** — clean for the 80%, but the 20% throws; org philosophy is non-throwing on the unhappy path.
- **D. Auto-detect from construction path** — magic; refactor (adding a smart-ctor) silently changes API shape.

**Why:** aligns with `[Validate]` / `[Authorize]` family pattern (explicit attribute = explicit shape). User picks; no surprise return types on refactor; `[TryMap]` plugs cleanly into existing `Result<T, E>` chains.

### 4. `MappingError` shape: tree-recursive (option C)

**Decision:**
```csharp
public readonly record struct MappingError(
    string Code,
    string PropertyPath,
    string? Reason = null,
    IReadOnlyList<MappingError>? Children = null);
```

- `Code` — structured string (`"mapping.constructor.threw"`, `"mapping.parse.failed"`, `"mapping.source.null"`, `"mapping.collection.elements_failed"`). Open-ended; generators / consumers can mint new codes without an enum-update PR.
- `PropertyPath` — `"Customer.Email"`, `"Items[5]"`, `"Order.Customer.Address.PostalCode"`. Pinpoint debugging info.
- `Reason` — human-readable detail, typically captured from inner `Exception.Message`.
- `Children` — nullable list. Single-error failures: `null`. Collection failures: per-element errors, each with `PropertyPath = "Items[i]"`. Nested-mapper failures: parent-with-one-child with parent path prepended.

**Rejected:**
- **A. Minimal `(Code, Reason?)`** — no property path; debugging deep mappings becomes opaque.
- **B. Separate types for collection vs single** — surface inconsistency between `Result<T, MappingError>` and `Result<List<T>, IReadOnlyList<MappingError>>`.
- **C-with-Inner-Exception** — leaks abstraction; class-eligible struct.
- **D. Strongly-typed kind enum** — closed set; can't extend without breaking change.

**Why:** single shape across single + collection + nested cases. `Children` tree maps naturally to nested DTOs. No allocation tax on the success path. `Mediator.Validation`'s `ValidationError` has the same shape — family consistency.

### 5. Collection failure shape: tree-recursive — same as MappingError (option C)

**Decision:** every `[TryMap]` returns `Result<TDst, MappingError>`. Collection failures use `MappingError.Children`.

```csharp
// Single-element [TryMap<A, B>]:
Result.Failure(new MappingError("mapping.constructor.threw", "Email", "invalid format"))

// Collection [TryMap<List<A>, List<B>>] with elements 5 and 17 failing:
Result.Failure(new MappingError(
    Code: "mapping.collection.elements_failed",
    PropertyPath: "(root)",
    Reason: "2 of 100 elements failed",
    Children: [
        new MappingError("mapping.constructor.threw", "Items[5].Email", "invalid format"),
        new MappingError("mapping.parse.failed", "Items[17].Quantity", "input string was not in a correct format")
    ]))
```

**Rejected:**
- **A. Fail-fast** — single-error semantic, but information loss on batch imports (100-row CSV → only row 1's error surfaces).
- **B. Different error type per cardinality** — `Result<T, MappingError>` vs `Result<List<T>, IReadOnlyList<MappingError>>`. Inconsistent surface.
- **D. Always-list** — `Result<T, IReadOnlyList<MappingError>>` for every `[TryMap]`; pays one extra alloc on every failure regardless of cardinality.

**Why:** uniformity across single + collection cases; richer structure than a flat list (parent error has the operation context, children carry details); easy to flatten via tree-walk if a consumer wants the flat report.

### 6. Null source handling: type-system driven (option C)

**Decision:** generator inspects the declared type-arg nullability.

- `[Map<OrderRequest, Order>]` (non-nullable source) → emits `static Order Map(OrderRequest src)`. Throws `ArgumentNullException` if caller passes null at runtime.
- `[Map<OrderRequest?, Order?>]` (explicit nullable source) → emits `static Order? Map(OrderRequest? src)`. Returns `null` if source is null.
- `[TryMap<OrderRequest, User>]` (non-nullable source) → null source is treated as a runtime failure: `Result.Failure(MappingError("mapping.source.null", "(root)"))`.

**Rejected:**
- **A. Always throw on null source** — no path to nullable maps.
- **B. Always pass through null** — viral; every chained map result becomes `T?`.

**Why:** type-system honesty — user opts into null at attribute-declaration time; downstream code doesn't have to keep null-checking infallible-non-null mappings; matches every other ZeroAlloc package's nullable stance.

## Components

### Runtime layer (`src/ZeroAlloc.Mapping/`, hand-coded ~80 LOC)

```
src/ZeroAlloc.Mapping/
├── Attributes/
│   ├── MapAttribute.cs                  // [Map<TSrc, TDst>]
│   ├── TryMapAttribute.cs               // [TryMap<TSrc, TDst>]
│   ├── MapPropertyAttribute.cs          // [MapProperty(src, dst)] — rename
│   ├── MapperIgnoreSourceAttribute.cs   // exclude source property
│   ├── MapperIgnoreTargetAttribute.cs   // exclude target property
│   └── MapValueAttribute.cs             // [MapValue(dst, constant)]
├── MappingError.cs                      // readonly record struct
├── PublicAPI.Shipped.txt
├── PublicAPI.Unshipped.txt
└── ZeroAlloc.Mapping.csproj
```

Type signatures:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class MapAttribute<TSource, TDestination> : Attribute { }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TryMapAttribute<TSource, TDestination> : Attribute { }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class MapPropertyAttribute(string sourceProperty, string targetProperty) : Attribute
{
    public string SourceProperty { get; } = sourceProperty;
    public string TargetProperty { get; } = targetProperty;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class MapValueAttribute(string targetProperty, object value) : Attribute
{
    public string TargetProperty { get; } = targetProperty;
    public object Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapperIgnoreSourceAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapperIgnoreTargetAttribute : Attribute { }

public readonly record struct MappingError(
    string Code,
    string PropertyPath,
    string? Reason = null,
    IReadOnlyList<MappingError>? Children = null);
```

### Generator (`src/ZeroAlloc.Mapping.Generator/`, `netstandard2.0`)

```
src/ZeroAlloc.Mapping.Generator/
├── MappingGenerator.cs           // IIncrementalGenerator entry point
├── MapperDiscovery.cs            // walk compilation, find [Map]/[TryMap]-decorated static partial classes
├── MapperModel.cs                // record models — MapperClass, MappingDecl, PropertyMapping
├── PropertyMatcher.cs            // by-name property matching with type compatibility
├── ConversionResolver.cs         // BCL conversions: cast, ctor, Parse, FromX, etc.
├── MapEmitter.cs                 // emit [Map] (direct, throwing) static method
├── TryMapEmitter.cs              // emit [TryMap] (Result, try/catch) static method
├── CollectionMappingEmitter.cs   // emit IEnumerable<T> → IEnumerable<T> per-element loops
├── NestedMappingResolver.cs      // wire nested-type mappings to other declared [Map]/[TryMap]
├── Diagnostics.cs                // ZAMP001-008 descriptors
├── IsExternalInit.cs             // netstandard2.0 polyfill (record init setters)
└── ZeroAlloc.Mapping.Generator.csproj
```

### Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| `ZAMP001` | Error | Required destination property has no source (no name match, no `[MapProperty]` rename, no `[MapValue]` constant). |
| `ZAMP002` | Error | Source/destination property pair has no conversion path (no implicit/explicit cast, no single-arg ctor, no `Parse`, no nested mapper). |
| `ZAMP003` | Error | Two source properties match the same destination after `[MapProperty]` resolution (ambiguous). |
| `ZAMP004` | Error | `[Map]` chain references a `[TryMap]`-only mapper (parent claims infallible, nested is fallible — mismatch). |
| `ZAMP005` | Warning | `[MapProperty]` references a non-existent source or destination property name. |
| `ZAMP006` | Error | `[Map]` / `[TryMap]` on a non-`static partial class` host. |
| `ZAMP007` | Error | Nullable source mapped to non-nullable destination under `[Map]` (use `[TryMap]` or `[MapValue]` fallback). |
| `ZAMP008` | Error | Constructor selection ambiguous (two ctors with equal preference; use `[MapperIgnoreSource]` or `[MapProperty]` to disambiguate). |

`ZAMP*` namespace reserved exclusively for `ZeroAlloc.Mapping`.

## Data flow

### Compile-time

1. User declares `[Map<,>]` / `[TryMap<,>]` on a `static partial class`.
2. `MappingGenerator.Initialize` registers a syntax provider matching class declarations bearing the attributes.
3. `MapperDiscovery` extracts `MapperClass` models — `(TSrc, TDst, Kind ∈ {Map, TryMap})` per declaration.
4. For each `(TSrc, TDst)`, `PropertyMatcher` walks `TDst`'s required + optional properties:
   - `[MapValue]` constant override → emit constant.
   - `[MapProperty]` rename → match named source property.
   - By-name match (case-sensitive in v1) → match `TSrc` property of same name.
   - `[MapperIgnoreTarget]` → skip (must have default value or be optional).
   - No match → emit `ZAMP001`.
5. For each match, `ConversionResolver` picks the conversion path: identity, implicit/explicit cast, single-arg ctor, `Parse(string, CultureInfo.InvariantCulture)`, static factory, nested mapper, collection-element loop. None found → `ZAMP002`.
6. `MapEmitter` / `TryMapEmitter` writes the static method body to `<ClassName>.<MethodName>.<src>_to_<dst>.g.cs`.

### Runtime — `[Map]` (throwing)

```csharp
public static Order Map(OrderRequest src)
{
    ArgumentNullException.ThrowIfNull(src);
    return new Order(
        id: new OrderId(src.Id),                        // smart-ctor; throws if invalid
        customerEmail: new Email(src.CustomerEmail),    // smart-ctor; throws if invalid
        items: MapItems(src.Items),                     // private helper for collection
        placedAt: src.PlacedAt
    );
}

private static List<OrderItem> MapItems(IReadOnlyList<OrderItemRequest> src)
{
    var dst = new List<OrderItem>(src.Count);   // pre-sized
    for (int i = 0; i < src.Count; i++)         // index-based, no enumerator alloc
        dst.Add(Map(src[i]));
    return dst;
}
```

Zero allocation on the construction path beyond the destination object itself. No closures, no boxing, no LINQ.

### Runtime — `[TryMap]` (Result-returning)

```csharp
public static Result<User, MappingError> TryMap(RegisterUserRequest src)
{
    if (src is null)
        return Result<User, MappingError>.Failure(
            new MappingError("mapping.source.null", "(root)", "source was null"));

    UserId userId;
    try { userId = new UserId(src.Id); }
    catch (Exception ex) {
        return Result<User, MappingError>.Failure(
            new MappingError("mapping.constructor.threw", "Id", ex.Message));
    }

    Email email;
    try { email = new Email(src.Email); }
    catch (Exception ex) {
        return Result<User, MappingError>.Failure(
            new MappingError("mapping.constructor.threw", "Email", ex.Message));
    }

    // ...continues for each fallible step, accumulating PropertyPath...

    return Result<User, MappingError>.Success(new User(userId, email, ...));
}
```

For collections under `[TryMap]`: per-element try/catch into a `List<MappingError>` accumulator; only on non-empty accumulator return `MappingError("mapping.collection.elements_failed", "Items", Children: accumulator)`.

For nested `[TryMap]` chain: `result.IsFailure` short-circuits with `Children = [innerError]` and `PropertyPath` prefixed with the parent property name.

### Allocation gate budget table (v1)

| API | Iterations | Budget |
|---|---:|---:|
| `[Map]` flat (4 properties, identity copy) | 1000 | 0 B beyond destination |
| `[Map]` with smart-ctor (1 fallible step) — happy path | 1000 | 0 B beyond destinations |
| `[Map]` with nested mapper — happy path | 1000 | 0 B beyond destinations |
| `[TryMap]` happy path (no throws) | 1000 | 0 B beyond destinations |
| `[TryMap]` collection (50 elements, all succeed) | 100 | 0 B beyond destinations + the result List |

Deny-path allocation (exception construction in `[TryMap]`) intentionally uncapped — matches `Mediator.Authorization`'s precedent.

## Error handling — edge cases beyond the deny path

1. **`[Map]` chain references a `[TryMap]`-only mapper** → `ZAMP004` error. No silent auto-promote.
2. **Nullable source / non-nullable destination property under `[Map]`** → `ZAMP007` error. Forces explicit decision (use `[TryMap]`, `[MapValue]` fallback, or change destination to nullable).
3. **Constructor selection ambiguity** → `ZAMP008` error with both candidates listed.
4. **Type mismatch with no conversion path** → `ZAMP002` error per missing destination property.
5. **`Parse` always uses `CultureInfo.InvariantCulture`** — locale-dependent parsing in a mapper is a footgun; users wanting locale-aware parsing pre-process their source.
6. **Collection of fallible elements under `[Map]`** → `ZAMP004` (parent claims infallible, element-level mapper is fallible).

## Testing

Three test projects.

**1. `tests/ZeroAlloc.Mapping.Tests`** — runtime types + integration:
- `MappingError` shape (record-struct equality, `Children` walk).
- `[Map]` happy path (flat, smart-ctor, nested, collection, all per-method attributes).
- `[Map]` failure path (smart-ctor throws bubbles uncaught — verifies no try/catch under `[Map]`).
- `[TryMap]` happy path (identical scenarios, asserting `Result.Success(T)` + zero error allocation).
- `[TryMap]` failure path (smart-ctor throws → `MappingError("mapping.constructor.threw", "Email", ...)`; null source → `MappingError("mapping.source.null", "(root)", ...)`; nested failure surfaces with `Children = [innerError]` and prepended `PropertyPath`; collection failures aggregated with per-element `PropertyPath = "Items[5]"`, etc.).
- Constructor selection (most-matching wins; tie → fewer params wins; ambiguous → `ZAMP008`).
- Type conversions (identity, implicit/explicit cast, single-arg ctor, `Parse`, `Enum.Parse`).
- Null handling (non-nullable arg throws on null; nullable arg returns null).

**2. `tests/ZeroAlloc.Mapping.Generator.Tests`** — generator snapshots (xUnit + Verify):
- `flat-map.verified.cs`, `flat-trymap.verified.cs`
- `nested-map.verified.cs`, `collection-map.verified.cs`
- `mapproperty-rename.verified.cs`, `mapvalue-constant.verified.cs`
- `mapper-ignore-source.verified.cs`, `nullable-passthrough.verified.cs`
- `smart-ctor-call.verified.cs`, `parse-conversion.verified.cs`
- One test per `ZAMP001..ZAMP008` (assertion-based, not snapshot).
- `Clean_Source_Emits_No_Diagnostics` negative-control.

**3. `samples/ZeroAlloc.Mapping.AotSmoke/`** — AOT + AllocationGate (per the day-one CI baseline; copy of the helper from `ZeroAlloc.Authorization`). Exercises `[Map]` and `[TryMap]` happy + deny paths, collection paths, and runs the gate over the 5 happy-path budget tests.

**Self-tests for the gate** ride along (3 negative-controls copied verbatim from `Mediator.Authorization`):
- `Gate_DetectsAllocation_WhenActionAllocates`
- `Gate_RejectsValueTask_NotCompletedSynchronously`
- `Gate_TolerantOfWarmupOnlyAllocations`

## Plumbing (mirror the family pattern)

### Top-level scaffolding

| File | Purpose |
|---|---|
| `Directory.Build.props` | Nullable + LangVersion + analyzer pack (Meziantou, Roslynator, ErrorProne, Hyperlinq, ZeroAlloc.Analyzers) + `WarningsAsErrors=$(WarningsAsErrors);RS0016;RS0017` + NuGet metadata |
| `Directory.Build.targets` | **Skip** — Mapping has no cross-package generator deps |
| `global.json` | Pinned SDK `10.0.x` |
| `GitVersion.yml` | Branch-based versioning |
| `LICENSE` | MIT |
| `release-please-config.json` | Single-package, `release-type: simple`, full `changelog-sections` |
| `.release-please-manifest.json` | `{ ".": "0.0.0" }` |
| `renovate.json` | Standard org config |
| `apicompat-suppressions.xml` | Empty initially |
| `.gitignore` | Standard .NET |
| `ZeroAlloc.Mapping.slnx` | Solution-file-XML (org standard) |
| `assets/icon.png` | Same as siblings |

### `.github/workflows/`

| File | Notes |
|---|---|
| `ci.yml` | `build` + `aot-smoke` + `api-compat` (uses shared `ZeroAlloc-Net/.github/.github/workflows/api-compat.yml@main`) |
| `release-please.yml` | Standard release-please-action + `publish` job |
| `publish-from-manifest.yml` | The rescue workflow (deployed across all repos today) — included from day one |

### Day-one CI baselines

- **Allocation gate** ships from PR #1.
- **PublicAPI tracking** active (RS0016/RS0017 as errors).
- **api-compat** active from first NuGet release.

## Out of scope (v1) — captured for future pickup

These will be seeded into `docs/backlog.md` of the new repo at scaffold time, with graduation signals per item, mirroring the family pattern (Authorization, Mediator backlogs).

| ID | Item | Why deferred |
|---|---|---|
| **B1** | **Flattening / unflattening** — `A.Customer.Address.City → B.City` | Mapperly's most-used feature, but rare in Clean-arch. Generator complexity is non-trivial. **Graduation:** a user explicitly asks, OR a template (Clean / CQRS-ES) needs it to ship. |
| **B2** | **Polymorphic / derived-type mapping** | Most family domains use sealed records. **Graduation:** real consumer hits this. |
| **B3** | **`IQueryable` projections** | `ZeroAlloc.Specification` already covers expression-tree construction. **Graduation:** EF Core users specifically ask AND Specification doesn't already cover it. |
| **B4** | **`[BeforeMap]` / `[AfterMap]` hooks** | Imperative hooks invite mutation. **Graduation:** real audit/logging need that can't be done at the call site. |
| **B5** | **Update-in-place on existing target** — `void Map(TSrc, TDst)` | Forces target mutability. **Graduation:** real consumer with hot-path entity tracking. |
| **B6** | **Reference handling / cycle detection** | Pure DTOs in vertical-slice don't form cycles. **Graduation:** real graph-shaped domain. |
| **B7** | **Private member mapping** | Encapsulation-breaking. **Graduation:** unlikely — documented as out-of-scope-by-design. |
| **B8** | **Generic mappings** — `[Map<List<T>, List<U>>]` parametrized | Generator complexity scales fast. **Graduation:** v1's per-concrete-type emission proves insufficient. |
| **B9** | **`[FormatProvider]` ToString customization** | `ToString()` in a mapper is a smell. **Graduation:** real consumer needs locale-aware formatting. |
| **B10** | **`IgnoreObsoleteMembersStrategy`** | YAGNI. **Graduation:** a user asks. |
| **B11** | **`UseDeepCloning` mode** | Niche. **Graduation:** real consumer hits this. |
| **B12** | **Reverse mapping auto-generation** — `[ReverseMap]` | Reversing non-trivial mappings rarely safe. **Graduation:** explicit ask for trivial-mapping reversal. |
| **B13** | **Case-insensitive property name matching** | Compile-time ambiguity risk. **Graduation:** real domain has case-mismatched DTOs from external sources. |
| **B14** | **Required-mapping strictness modes** (Mapperly's `RequiredMappingStrategy`) | v1 always errors on missing required destination property; source-side ignores by default. **Graduation:** stricter source-side enforcement requested. |

### What v1 does ship that's worth re-emphasizing

- **`ZeroAlloc.ValueObjects` smart-constructor integration** is "free" via the single-arg-ctor conversion path — no `[ValueObjectMap]` attribute needed.
- **`ZeroAlloc.Results` integration** is the entire `[TryMap]` story — first-class.
- **Collection-mapping fail-aggregation** under `[TryMap]` (tree-shaped `MappingError.Children`) is a real differentiator vs Mapperly's single-throw model.
- **Day-one AOT certification** with the allocation-gate pattern.

## Versioning

Initial release `1.0.0`. Standard family SemVer. The runtime + generator ship as ONE NuGet (`ZeroAlloc.Mapping`) — generator bundled as analyzer asset via `IncludeGeneratorInPackage` MSBuild target.
