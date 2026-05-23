# ZeroAlloc.Mapping — Backlog

Items deferred from v1.0.0. Each entry has a **Graduation signal** that, when met, promotes the item to a tracked issue. Until then, items live here as the canonical record of "considered, not yet built."

> **Update 2026-05-07:** B1 (flattening), B4 (hooks), B10 (`[Obsolete]` skip),
> B12 (`[ReverseMap]`), B13 (case-insensitive), B14 (strict source) graduated
> into v1 — see [`plans/2026-05-07-mapping-v1-extensions-design.md`](plans/2026-05-07-mapping-v1-extensions-design.md).

> **Update 2026-05-08:** B5 (update-in-place), B9 (`[MappingCulture]`), B2 (polymorphic dispatch)
> graduated into v1.2 — see [`plans/2026-05-08-mapping-v1.2-extensions-design.md`](plans/2026-05-08-mapping-v1.2-extensions-design.md).

> **Update 2026-05-08:** B8 (collection overloads, pragmatic interpretation) and B15
> (duplicate `[MappingCulture]` diagnostic) graduated into v1.3 — see
> [`plans/2026-05-08-mapping-v1.3-extensions-design.md`](plans/2026-05-08-mapping-v1.3-extensions-design.md).
> True open-generic mappings remain deferred (C# generic-attribute limitations).

> **Update 2026-05-23:** B3 (IQueryable projections), B6 (cycle-safe mapping),
> and B11 (deep-clone mode) graduated into v1.4 — see
> [`plans/2026-05-23-mapping-v1.4-extensions-design.md`](plans/2026-05-23-mapping-v1.4-extensions-design.md).
> B7 (private member mapping) remains the deliberate out-of-scope-by-design
> policy statement. B12 (DeepClone+CycleSafe integration) is a follow-up
> from v1.4 — see `B12` below.

For v1 scope, see [`plans/2026-05-07-mapping-design.md`](plans/2026-05-07-mapping-design.md).

---

## B7 — Private member mapping

**What.** Map to/from `private` properties or fields.

**Why deferred.** Encapsulation-breaking by design. Documented as out-of-scope-by-design.

**Graduation signal.** Unlikely — keep on the list as a public statement of policy.

---

## B12 — `DeepClone + CycleSafe` integration

**What.** When both `DeepClone = true` and `CycleSafe = true` are set on a `[Map<,>]`, emit the deep-clone literal walk with the runtime tracker threaded through every nested clone call. Currently the CycleSafe routing fork wins and DeepClone literal walks don't activate.

**Why deferred.** Combining the two emitters cleanly requires either (a) merging the walks into a single emit pipeline, or (b) calling DeepCloneEmitter with a tracker parameter that mutates the property-assignment emit. Both are non-trivial and weren't needed by the v1.4 graduation cases. Users who need deep-clone of a cyclic graph today can declare explicit nested `[Map<,>]` for each type — CycleSafe's ZAMP018 enforces full coverage.

**Graduation signal.** A real consumer hits the case where DeepClone+CycleSafe should literal-walk and finds the explicit-nested workaround too noisy.
