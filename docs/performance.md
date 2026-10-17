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
| `[Map<,>]` update-in-place | 0 | 1000 | No destination alloc; only field writes (caller supplies buffer) |
| `[Map<,>]` with `[MappingCulture]` | 256 | 1000 | `decimal.Parse` non-Invariant overhead (~160 B BCL) |
| `[PolymorphicMap<,>]` dispatch | 96 | 1000 | + runtime type-test |
| `[Map<,>]` `List<T>` overload (50 elements) | 2200 | 200 | List wrapper + backing array + 50 dst records |
| `[Map<,>]` `IEnumerable<T>` lazy | 96 | 1000 | LINQ `Select` iterator, no enumeration |

`tests/ZeroAlloc.Mapping.Tests/NestedCollectionBudgetTests.cs` holds a nested collection member to exactly its destination. Each budget is the measured cost of building the same destination by hand, so there is no headroom: a LINQ iterator, a closure or a re-grown list fails the test.

| Nested collection member, 16 elements | Budget | Covers |
|---|---|---|
| `[Map<,>]` | destination | `List<T>`, `T[]`, `IList<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `IReadOnlyCollection<T>`, `IEnumerable<T>`, and `List<T>` into `T[]` |
| `[Map<,>]` update-in-place | destination list | `List<T>` member |
| `[Map<,>]` / `[TryMap<,>]` element conversion | destination | `List<int>` into `List<long>`, `int[]` into `long[]` |
| `[Map<,>(CycleSafe = true)]` | tracker + destination | `List<T>` and `T[]` members |
| `[Map<,>(DeepClone = true)]` | destination | `List<T>` and `T[]` members |
| `[Map<,>(DeepClone = true, CycleSafe = true)]` | tracker + destination | `List<T>` of `T[]` members |

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

The harness lives at `benchmarks/ZeroAlloc.Mapping.Benchmarks/`. It compares four mappers across seven scenarios using BenchmarkDotNet's default JIT job on .NET 10, with `[MemoryDiagnoser]` enabled. The results below ran out of process in three launches on an otherwise idle machine, pinned to two performance cores; `Ratio` is each row's mean time relative to the hand-written mapper in the same run. Each scenario uses identical source/destination types across all mappers; AutoMapper's `IMapper` is built once in `[GlobalSetup]` so profile compilation is excluded from per-iteration cost. Hand-written rows use inline `new Dst(...)` with no helper indirection.

AutoMapper 16.x is commercially licensed; the benchmark project is dev-only and not distributed (`IsPackable=false`). Cited numbers are evaluation-fair-use only.

### Results

<!-- BENCH:START -->
_Last refreshed: 2026-10-17_

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-WGASOM : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000010100  LaunchCount=3  

```

> Allocations are exact. Speed is shown as same-run ratios against the hand-written mapper; absolute timings are not published.

### FlatIdentity

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 72 B |
| ZeroAlloc_ | 0.96 | 72 B |
| Mapperly_ | 0.98 | 72 B |
| AutoMapper_ | 3.93 | 72 B |

### FlatConversion

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 48 B |
| ZeroAlloc_ | 0.97 | 48 B |
| Mapperly_ | 0.93 | 48 B |
| AutoMapper_ | 1.28 | 48 B |

### Flattening

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 72 B |
| ZeroAlloc_ | 0.98 | 72 B |
| Mapperly_ | 0.98 | 72 B |
| AutoMapper_ | 4.34 | 72 B |

### Collection

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 78.18 KB |
| ZeroAlloc_ | 1.03 | 78.18 KB |
| Mapperly_ | 1.25 | 78.25 KB |
| AutoMapper_ | 1.57 | 86.52 KB |

### Polymorphic

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 32 B |
| ZeroAlloc_ | 1.00 | 32 B |
| Mapperly_ | 0.96 | 32 B |
| AutoMapper_ | 5.67 | 32 B |

### UpdateInPlace

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | - |
| ZeroAlloc_ | 0.88 | - |
| Mapperly_ | 0.98 | - |
| AutoMapper_ | 6.10 | - |

### TryMap

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 48 B |
| ZeroAlloc_ | 1.01 | 48 B |
| Mapperly_ | 0.96 | 48 B |

<!-- BENCH:END -->

### Caveats

- **Allocation column, checked per thread.** In this run BenchmarkDotNet reported 0 B for three rows in which Gen0 collections happened: FlatConversion Mapperly, Polymorphic AutoMapper and TryMap HandWritten. Re-runs moved the false zeros to other rows, and once reported 530 B and 1,167 B for a 48 B row, in the two scenarios that parse dates and numbers. Every row in those scenarios was re-measured with `GC.GetAllocatedBytesForCurrentThread()` over a million calls, the method `AllocationGate` uses, and the table shows those values: each conversion row allocates exactly its 48 B result. `tools/import-benchmarks.ps1` now refuses to publish a row that reports no allocation while Gen0 collections happened.

- **TryMap row, Mapperly column.** Mapperly has no native fallible-mapping primitive; this row measures its non-fallible `Map` call as a reference point for the cost ZA's `TryMap` pays beyond happy-path mapping. AutoMapper is omitted entirely since it has no equivalent.
- **Collection row, Mapperly column.** The Collection scenario's Mapperly row uses `Select(...).ToList()` rather than Mapperly's auto-emitted `partial List<FlatDst> Map(List<FlatSrc>)` overload. Numbers reflect the LINQ-fallback path, not the bulk-overload path.

### Reproducing

```bash
dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*" --launchCount 3
pwsh tools/import-benchmarks.ps1
```

The first command takes ~30 minutes; the second is instant, and keeps only the ratio and allocation columns. Re-commit `docs/performance.md` after the splice.

```bash
# If you switched branches recently, clear stale generator output:
rm -rf benchmarks/ZeroAlloc.Mapping.Benchmarks/generated
```

### Reading the numbers

- **HandWritten** is the speed-of-light. ZeroAlloc.Mapping should match it (or be within noise) for FlatIdentity and UpdateInPlace.
- **Mapperly** should be within ~30% of ZeroAlloc.Mapping on most scenarios — both are source generators emitting nearly identical IL, so any large gap is worth investigating. In this run it lands at 0.95×–1.21× ZeroAlloc.Mapping's time.
- **AutoMapper** lands 1.3×–5.7× the hand-written baseline on the single-object scenarios above and 6.1× on update-in-place, where ZeroAlloc.Mapping stays within 0.88×–1.01× of it. On the 1,000-item collection AutoMapper takes 1.57× and allocates 86.52 KB against 78.18 KB; on every other scenario all four mappers allocate the same. The time gap is the cost of runtime expression-tree compilation and per-call rule lookup.
- The `Allocated` column is the load-bearing one for "zero-allocation" claims. If ZeroAlloc.Mapping shows non-zero bytes, the destination type itself is being measured (records allocate; structs don't) — see the budget table above for per-shape baselines.
- Differences of a few percent between the source-generated rows are within the spread between launches; the headline is the UpdateInPlace ratio (6.10× for AutoMapper).

### What's not measured here

- **Startup tax** — AutoMapper's first-call profile compilation (~tens of ms) isn't reflected in steady-state numbers. If you map once per process lifetime, the comparison flips.
- **AOT** — BDN doesn't run under Native AOT; `samples/ZeroAlloc.Mapping.AotSmoke/` covers correctness there. The generated mapping body is identical between JIT and AOT, so steady-state perf is the same.
- **Multi-threaded contention** — single-threaded numbers only. Mapping has no shared mutable state; multi-threaded would just measure thread-pool noise.

## Where to next

- Diagnostics: [Diagnostics](diagnostics.md).
- Testing: [Testing](testing.md).
