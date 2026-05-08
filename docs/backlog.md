# ZeroAlloc.Mapping — Backlog

Items deferred from v1.0.0. Each entry has a **Graduation signal** that, when met, promotes the item to a tracked issue. Until then, items live here as the canonical record of "considered, not yet built."

> **Update 2026-05-07:** B1 (flattening), B4 (hooks), B10 (`[Obsolete]` skip),
> B12 (`[ReverseMap]`), B13 (case-insensitive), B14 (strict source) graduated
> into v1 — see [`plans/2026-05-07-mapping-v1-extensions-design.md`](plans/2026-05-07-mapping-v1-extensions-design.md).

> **Update 2026-05-08:** B5 (update-in-place), B9 (`[MappingCulture]`), B2 (polymorphic dispatch)
> graduated into v1.2 — see [`plans/2026-05-08-mapping-v1.2-extensions-design.md`](plans/2026-05-08-mapping-v1.2-extensions-design.md).

For v1 scope, see [`plans/2026-05-07-mapping-design.md`](plans/2026-05-07-mapping-design.md).

---

## B3 — `IQueryable` projections

**What.** Compile-time `Expression<Func<TSrc, TDst>>` emission for use with EF Core's `Select(...)`.

**Why deferred.** `ZeroAlloc.Specification` already covers expression-tree construction; double coverage risks divergence.

**Graduation signal.** EF Core users specifically ask AND `ZeroAlloc.Specification` doesn't already cover it.

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

## B15 — Diagnostic for duplicate `[MappingCulture]` declarations across partial-class parts

**What.** `[MappingCulture]` is `AllowMultiple = false`, but a class declared in two partial-class parts can carry the attribute on each. The C# compiler does not reject this (different syntax sites), and v1.2's `MapperDiscovery` silently picks the first one via `break` and ignores the rest.

**Why deferred.** Edge case unlikely to surface in real codebases. Detected during v1.2 final review (M8). Acceptable v1.2 behavior since the chosen culture is deterministic (declaration order).

**Graduation signal.** Real consumer hits silent-ignore confusion, OR ZAMP016 candidate when v1.3 cleans up duplicate-attribute diagnostics across the family.

---

## B11 — `UseDeepCloning` mode

**What.** Default to deep-cloning collections and nested objects instead of shallow copy.

**Why deferred.** Niche — opaque deep-cloning makes generated code hard to audit. Shallow-by-default + explicit nested `[Map]` declarations is the family idiom.

**Graduation signal.** Real consumer hits this.
