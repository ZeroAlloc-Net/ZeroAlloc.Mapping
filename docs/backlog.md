# ZeroAlloc.Mapping — Backlog

Items deferred from v1.0.0. Each entry has a **Graduation signal** that, when met, promotes the item to a tracked issue. Until then, items live here as the canonical record of "considered, not yet built."

For v1 scope, see [`plans/2026-05-07-mapping-design.md`](plans/2026-05-07-mapping-design.md).

---

## B1 — Flattening / unflattening

**What.** Auto-flatten nested-source paths into flat-target properties (and vice versa). E.g. `A.Customer.Address.City → B.City` without an explicit `[MapProperty]` rename.

**Why deferred.** Mapperly's most-used feature, but rare in Clean-architecture codebases — flat DTOs typically come from flat queries. Generator complexity is non-trivial: requires a path-walker that builds nested null-checks and a name-collision policy.

**Graduation signal.** A user explicitly asks for it, OR a template (`za-clean` / CQRS-ES) needs it to ship.

---

## B2 — Polymorphic / derived-type mapping

**What.** `[Map<Animal, AnimalDto>]` that dispatches at runtime to `Map<Cat, CatDto>` / `Map<Dog, DogDto>` based on the source's runtime type.

**Why deferred.** Most family domains use sealed records (no polymorphism). Adds runtime type checks to the hot path, eroding the zero-allocation guarantee.

**Graduation signal.** A real consumer hits this — typically a domain with abstract base + sealed derived hierarchy that needs uniform DTO output.

---

## B3 — `IQueryable` projections

**What.** Compile-time `Expression<Func<TSrc, TDst>>` emission for use with EF Core's `Select(...)`.

**Why deferred.** `ZeroAlloc.Specification` already covers expression-tree construction; double coverage risks divergence.

**Graduation signal.** EF Core users specifically ask AND `ZeroAlloc.Specification` doesn't already cover it.

---

## B4 — `[BeforeMap]` / `[AfterMap]` hooks

**What.** User-defined static methods invoked before/after the generated body to apply imperative side-effects (audit log, tracing).

**Why deferred.** Imperative hooks invite mutation and make generated code harder to reason about. Caller-side wrapping covers the common cases.

**Graduation signal.** Real audit/logging need that can't be done at the call site (e.g. multi-mapper telemetry that must be DRY).

---

## B5 — Update-in-place on existing target

**What.** `void Map(TSrc src, TDst existingDst)` — mutate an already-allocated destination instead of constructing a new one.

**Why deferred.** Forces target mutability, conflicting with the `record`/immutable-DTO default in the family.

**Graduation signal.** Real consumer with hot-path entity-tracking pattern (e.g. EF Core change-tracker integration).

---

## B6 — Reference handling / cycle detection

**What.** Detect and break cycles when source graph contains back-references.

**Why deferred.** Pure DTOs in vertical-slice architecture don't form cycles. Detection adds per-call dictionary tracking, defeating the zero-allocation guarantee.

**Graduation signal.** Real graph-shaped domain (e.g. ORM-heavy aggregate with parent/child back-refs).

---

## B7 — Private member mapping

**What.** Map to/from `private` properties or fields.

**Why deferred.** Encapsulation-breaking by design. Documented as out-of-scope-by-design.

**Graduation signal.** Unlikely — keep on the list as a public statement of policy.

---

## B8 — Generic mappings — `[Map<List<T>, List<U>>]` parametrized

**What.** Class-level attribute parametrized by an open generic, generator emits a generic method.

**Why deferred.** Generator complexity scales fast (multiple type-parameter substitution paths, constraint propagation, AOT-trim interactions).

**Graduation signal.** v1's per-concrete-type emission proves insufficient — a real consumer has dozens of `List<T>` mappings differing only by `T`.

---

## B9 — `[FormatProvider]` ToString customization

**What.** Per-property `IFormatProvider` customisation for `ToString()` conversions.

**Why deferred.** `ToString()` in a mapper is a smell — formatting belongs at the presentation boundary.

**Graduation signal.** Real consumer needs locale-aware formatting that can't be deferred to the UI/JSON layer.

---

## B10 — `IgnoreObsoleteMembersStrategy`

**What.** Auto-skip `[Obsolete]` members during property matching.

**Why deferred.** YAGNI. `[MapperIgnoreSource/Target]` covers the explicit-opt-out path.

**Graduation signal.** A user asks.

---

## B11 — `UseDeepCloning` mode

**What.** Default to deep-cloning collections and nested objects instead of shallow copy.

**Why deferred.** Niche — opaque deep-cloning makes generated code hard to audit. Shallow-by-default + explicit nested `[Map]` declarations is the family idiom.

**Graduation signal.** Real consumer hits this.

---

## B12 — Reverse mapping auto-generation — `[ReverseMap]`

**What.** Class-level `[ReverseMap<TSrc, TDst>]` that emits both `Map(TSrc) → TDst` and `Map(TDst) → TSrc`.

**Why deferred.** Reversing non-trivial mappings (those with `[MapValue]`, `[MapProperty]`, dropped properties) is rarely safe — easy to silently lose data.

**Graduation signal.** Explicit ask for trivial-mapping reversal (DTO ↔ command for symmetric CRUD).

---

## B13 — Case-insensitive property name matching

**What.** Match `src.foo` to `dst.Foo` when names differ only by case.

**Why deferred.** Compile-time ambiguity risk: `src.Foo` and `src.foo` both target `dst.Foo` would silently pick one.

**Graduation signal.** Real domain has case-mismatched DTOs from external sources (PHP/JSON-snake-case integrations).

---

## B14 — Required-mapping strictness modes (Mapperly's `RequiredMappingStrategy`)

**What.** Per-mapping or global toggle for "all source properties must be consumed" vs "all destination required properties must be set".

**Why deferred.** v1 always errors on missing required destination property; source-side ignores by default. Stricter source-side enforcement is the rare ask.

**Graduation signal.** Stricter source-side enforcement requested.
