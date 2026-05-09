---
id: performance
title: Performance
description: Zero-allocation internals, allocation-budget table, and Native AOT compatibility.
sidebar_position: 11
---

# Performance

## Why zero-alloc?

The generator emits direct property assignments at compile time. There is no reflection, no `Expression<Func<>>` compilation, no per-call dictionary lookup, and no virtual dispatch through a runtime mapper interface. A flat `Map(src)` resolves to a single `new Dst(src.A, src.B, ...)` expression.

Per-call allocation therefore reduces to:

- The destination instance itself (~24-80 B for a small record).
- Any conversion path's BCL allocations (`decimal.Parse` with a non-Invariant culture, `int.Parse(string)`, etc.).
- Zero closure or delegate overhead — the generated body is plain straight-line code.

`[TryMap<,>]` adds the `Result<TDestination, MappingError>` wrapper allocation; `[BeforeMap]`/`[AfterMap]` hooks add nothing if the bodies are empty.

## Allocation budget

Every entry below is enforced by `tests/ZeroAlloc.Mapping.Tests/AllocationBudgetTests.cs`. The CI gate fails if any path regresses.

| Path | Budget (B/call) | Iterations | Notes |
|---|---|---|---|
| `[Map<,>]` flat | 80 | 1000 | Destination record only |
| `[Map<,>]` flat repeated | 80 | 5000 | Same path, more iterations |
| `[TryMap<,>]` happy | 120 | 1000 | + `Result<,>` wrapper |
| `[TryMap<,>]` happy repeated | 120 | 5000 | Sustained throughput |
| `[TryMap<,>]` deny | 1024 | 1000 | Exception + stack-trace string dominate |
| `[Map<,>]` with hooks | 80 | 1000 | Empty `[BeforeMap]`/`[AfterMap]` add no overhead |
| `[ReverseMap<,>]` forward | 80 | 1000 | Same as flat — desugars to `[Map<,>]` |
| `[Map<,>]` flatten | 80 | 1000 | `Outer.Nested.Value` is a property access, no allocation |
| `[Map<,>]` update-in-place | 80 | 1000 | No destination alloc; only field writes (caller supplies buffer) |
| `[Map<,>]` with `[MappingCulture]` | 256 | 1000 | `decimal.Parse` non-Invariant overhead (~160 B BCL) |
| `[PolymorphicMap<,>]` dispatch | 96 | 1000 | + runtime type-test |
| `[Map<,>]` `List<T>` overload (50 elements) | 2200 | 200 | List wrapper + backing array + 50 dst records |
| `[Map<,>]` `IEnumerable<T>` lazy | 96 | 1000 | LINQ `Select` iterator, no enumeration |

A few notes pulled directly from the test fixtures:

- The deny path's 1024 B/call cap is a generous-but-tight bound dominated by the exception's stack-trace string. The point is not "tight" — it's "not 10 KB".
- The `[MappingCulture]` 256 B budget reflects `decimal.Parse(string, IFormatProvider)` allocating ~160 B for its internal `NumberBuffer` when handed a non-Invariant culture; the destination record itself is ~24 B. `CultureInfo.GetCultureInfo` is cached.
- The 50-element `List<T>` overload measures ~2056 B/call on .NET 10: 1200 B for fifty destination records, ~424 B for the `T[]` backing array (allocated eagerly by `List<T>(capacity)`), and a ~40 B `List<T>` header. Budget set at 2200 B for headroom.
- The `IEnumerable<T>` overload returns `Enumerable.Select` and allocates only the `SelectIListIterator<,>` (~56 B) plus a captured delegate. No mapping work happens until the consumer enumerates.

## Methodology

The gate is `samples/ZeroAlloc.Mapping.AotSmoke/Internal/AllocationGate.cs`:

```csharp
public static void AssertBudget(int budgetBytes, int iterations, Action action, string label);
```

It warms with two action calls, forces a full GC, snapshots `GC.GetAllocatedBytesForCurrentThread()`, runs the action `iterations` times, and asserts the delta is less than `budgetBytes * iterations`. The thread-local counter avoids cross-test interference; the warm-up tolerates static-ctor / first-call JIT cost.

Typical use:

```csharp
[Fact]
public void Map_FlatRecord_WithinBudget()
{
    var req = new OrderRequest(42, "n");
    AllocationGate.AssertBudget(80, 1000,
        () => _ = BudgetMappings.Map(req),
        "[Map<OrderRequest, Order>]");
}
```

The harness lives in the AOT smoke sample because it's also exercised under Native AOT — the same gate that catches a regression in Debug also catches it in a published binary.

## Native AOT

Fully compatible. The AOT smoke binary at `samples/ZeroAlloc.Mapping.AotSmoke/` exercises every feature documented across the rest of the manual under `<PublishAot>true</PublishAot>` and treats trim/AOT warnings (`IL2026`, `IL2067`, `IL2075`, `IL2091`, `IL3050`, `IL3051`) as errors. CI's `aot-smoke` job is the ground truth: if the binary publishes and runs there, AOT is fine.

One configuration note: `[MappingCulture]` requires real culture data, so the AOT sample sets:

```xml
<InvariantGlobalization>false</InvariantGlobalization>
```

Without it, `CultureInfo.GetCultureInfo("nl-NL")` resolves to the invariant culture and `decimal.Parse("12,34")` will misparse. If your AOT app does not use `[MappingCulture]`, leave invariant globalization on and ship a smaller binary.

The generator emits no reflection, no `MakeGenericType`, no `Activator.CreateInstance`, and no expression compilation. There is nothing for the trimmer to be uncertain about.

## Comparison with reflection-based mappers

Reflection-based mappers (AutoMapper, Mapster's expression mode) build a runtime expression tree the first time a pair is mapped and cache it. That has three costs source generators avoid:

- **Startup**: the first call pays an expression-compilation hit (tens of milliseconds in pathological cases). The generator's first call is the same as the millionth — straight-line code, JITted once.
- **Allocations**: cached expression trees still box value-typed property reads in some configurations and allocate per-call closures over rule lookups. The generator's emitted body has no boxing and no closures.
- **AOT compatibility**: reflection-based mappers either don't work under Native AOT or require extensive `[DynamicallyAccessedMembers]` annotations and trim hints. Source generators emit code the trimmer can see and prove safe.

Mapperly is the closest peer — also a source generator, also zero-reflection. Concrete BenchmarkDotNet numbers comparing all four (ZeroAlloc.Mapping, Mapperly, AutoMapper, hand-written) are below.

## Benchmarks

### Methodology

The harness lives at `benchmarks/ZeroAlloc.Mapping.Benchmarks/`. It compares four mappers across seven scenarios using BenchmarkDotNet's default JIT job on .NET 10, with `[MemoryDiagnoser]` enabled. Each scenario uses identical source/destination types across all mappers; AutoMapper's `IMapper` is built once in `[GlobalSetup]` so profile compilation is excluded from per-iteration cost. Hand-written rows use inline `new Dst(...)` with no helper indirection.

### Results

<!-- BENCH:START -->
_Results not yet imported. Run `tools/import-benchmarks.ps1`._
<!-- BENCH:END -->

### Reproducing

```bash
dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*"
pwsh tools/import-benchmarks.ps1
```

The first command takes ~15 minutes; the second is instant. Re-commit `docs/performance.md` after the splice.

### Reading the numbers

- **HandWritten** is the speed-of-light. ZeroAlloc.Mapping should match it (or be within noise) for FlatIdentity and UpdateInPlace.
- **Mapperly** should be within ~30% of ZeroAlloc.Mapping on most scenarios — both are source generators emitting nearly identical IL, so any large gap is worth investigating.
- **AutoMapper** typically lands 10x–100x slower with non-zero allocation everywhere. That gap is the cost of runtime expression-tree compilation and per-call rule lookup.
- The `Allocated` column is the load-bearing one for "zero-allocation" claims. If ZeroAlloc.Mapping shows non-zero bytes, the destination type itself is being measured (records allocate; structs don't) — see the budget table above for per-shape baselines.

### What's not measured here

- **Startup tax** — AutoMapper's first-call profile compilation (~tens of ms) isn't reflected in steady-state numbers. If you map once per process lifetime, the comparison flips.
- **AOT** — BDN doesn't run under Native AOT; `samples/ZeroAlloc.Mapping.AotSmoke/` covers correctness there. The generated mapping body is identical between JIT and AOT, so steady-state perf is the same.
- **Multi-threaded contention** — single-threaded numbers only. Mapping has no shared mutable state; multi-threaded would just measure thread-pool noise.

## Where to next

- Diagnostics: [Diagnostics](diagnostics.md).
- Testing: [Testing](testing.md).
