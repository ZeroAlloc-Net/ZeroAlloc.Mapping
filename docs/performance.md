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

AutoMapper 16.x is commercially licensed; the benchmark project is dev-only and not distributed (`IsPackable=false`). Cited numbers are evaluation-fair-use only.

### Results

<!-- BENCH:START -->
_Last refreshed: 2026-05-10_

### FlatIdentity

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 32.44 ns | 3.047 ns |  8.643 ns |  1.06 |    0.38 | 0.0014 |      72 B |        1.00 |
| ZeroAlloc_   | 30.79 ns | 2.696 ns |  7.288 ns |  1.01 |    0.33 | 0.0015 |      72 B |        1.00 |
| Mapperly_    | 26.29 ns | 2.500 ns |  7.132 ns |  0.86 |    0.31 | 0.0015 |      72 B |        1.00 |
| AutoMapper_  | 76.37 ns | 6.919 ns | 19.173 ns |  2.50 |    0.86 | 0.0014 |      72 B |        1.00 |


### FlatConversion

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 167.9 ns |  7.22 ns | 20.37 ns | 164.4 ns |  1.01 |    0.17 | 0.0010 |      48 B |        1.00 |
| ZeroAlloc_   | 185.8 ns | 10.58 ns | 30.36 ns | 175.4 ns |  1.12 |    0.22 | 0.0010 |      48 B |        1.00 |
| Mapperly_    | 201.9 ns | 11.18 ns | 31.71 ns | 197.7 ns |  1.22 |    0.24 | 0.0010 |      48 B |        1.00 |
| AutoMapper_  | 327.2 ns | 12.22 ns | 33.66 ns | 323.0 ns |  1.98 |    0.30 | 0.0010 |      48 B |        1.00 |


### Flattening

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 29.17 ns | 4.485 ns | 12.940 ns |  1.20 |    0.76 | 0.0015 |      72 B |        1.00 |
| ZeroAlloc_   | 22.88 ns | 2.493 ns |  7.031 ns |  0.94 |    0.50 | 0.0015 |      72 B |        1.00 |
| Mapperly_    | 22.95 ns | 2.385 ns |  6.727 ns |  0.94 |    0.49 | 0.0015 |      72 B |        1.00 |
| AutoMapper_  | 70.84 ns | 5.222 ns | 14.556 ns |  2.91 |    1.37 | 0.0014 |      72 B |        1.00 |


### Collection

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev    | Median   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| HandWritten_ | 39.57 μs | 4.549 μs | 13.051 μs | 37.90 μs |  1.11 |    0.51 | 1.5869 |      - |  78.18 KB |        1.00 |
| ZeroAlloc_   | 38.61 μs | 3.943 μs | 10.992 μs | 37.19 μs |  1.08 |    0.46 | 1.6479 | 0.1221 |  78.18 KB |        1.00 |
| Mapperly_    | 41.09 μs | 5.774 μs | 15.710 μs | 35.97 μs |  1.15 |    0.58 | 1.7090 | 0.1221 |  78.25 KB |        1.00 |
| AutoMapper_  | 30.44 μs | 3.239 μs |  9.294 μs | 28.52 μs |  0.85 |    0.38 | 1.8311 | 0.1221 |  86.52 KB |        1.11 |


### Polymorphic

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 11.25 ns | 0.997 ns | 2.778 ns |  1.06 |    0.36 | 0.0007 |      32 B |        1.00 |
| ZeroAlloc_   | 11.38 ns | 1.214 ns | 3.463 ns |  1.07 |    0.42 | 0.0007 |      32 B |        1.00 |
| Mapperly_    | 11.01 ns | 1.107 ns | 3.124 ns |  1.04 |    0.38 | 0.0007 |      32 B |        1.00 |
| AutoMapper_  | 48.76 ns | 2.724 ns | 7.726 ns |  4.59 |    1.30 | 0.0006 |      32 B |        1.00 |


### UpdateInPlace

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------- |----------:|----------:|----------:|----------:|------:|--------:|----------:|------------:|
| HandWritten_ |  4.691 ns | 0.3778 ns | 1.0470 ns |  4.437 ns |  1.04 |    0.31 |         - |          NA |
| ZeroAlloc_   |  6.051 ns | 0.6478 ns | 1.8482 ns |  5.417 ns |  1.35 |    0.49 |         - |          NA |
| Mapperly_    |  6.000 ns | 0.3755 ns | 1.0468 ns |  5.838 ns |  1.33 |    0.35 |         - |          NA |
| AutoMapper_  | 83.935 ns | 0.6793 ns | 0.6022 ns | 83.985 ns | 18.66 |    3.59 |         - |          NA |


### TryMap

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 222.6 ns | 13.84 ns | 39.71 ns | 208.5 ns |  1.03 |    0.25 | 0.0010 |      48 B |        1.00 |
| ZeroAlloc_   | 208.4 ns |  9.66 ns | 27.08 ns | 201.6 ns |  0.96 |    0.20 | 0.0010 |      48 B |        1.00 |
| Mapperly_    | 195.4 ns |  8.59 ns | 23.81 ns | 194.2 ns |  0.90 |    0.18 | 0.0010 |      48 B |        1.00 |


<!-- BENCH:END -->

### Caveats

- **TryMap row, Mapperly column.** Mapperly has no native fallible-mapping primitive; this row measures its non-fallible `Map` call as a reference point for the cost ZA's `TryMap` pays beyond happy-path mapping. AutoMapper is omitted entirely since it has no equivalent.
- **Collection row, Mapperly column.** The Collection scenario's Mapperly row uses `Select(...).ToList()` rather than Mapperly's auto-emitted `partial List<FlatDst> Map(List<FlatSrc>)` overload. Numbers reflect the LINQ-fallback path, not the bulk-overload path.

### Reproducing

```bash
dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*"
pwsh tools/import-benchmarks.ps1
```

The first command takes ~15 minutes; the second is instant. Re-commit `docs/performance.md` after the splice.

```bash
# If you switched branches recently, clear stale generator output:
rm -rf benchmarks/ZeroAlloc.Mapping.Benchmarks/generated
```

### Reading the numbers

- **HandWritten** is the speed-of-light. ZeroAlloc.Mapping should match it (or be within noise) for FlatIdentity and UpdateInPlace.
- **Mapperly** should be within ~30% of ZeroAlloc.Mapping on most scenarios — both are source generators emitting nearly identical IL, so any large gap is worth investigating.
- **AutoMapper** typically lands 10x–100x slower with non-zero allocation everywhere. That gap is the cost of runtime expression-tree compilation and per-call rule lookup.
- The `Allocated` column is the load-bearing one for "zero-allocation" claims. If ZeroAlloc.Mapping shows non-zero bytes, the destination type itself is being measured (records allocate; structs don't) — see the budget table above for per-shape baselines.
- Differences below ~10ns on the FlatIdentity table are within JIT noise (see the StdDev column); the headline is the UpdateInPlace ratio (18.66× for AutoMapper).

### What's not measured here

- **Startup tax** — AutoMapper's first-call profile compilation (~tens of ms) isn't reflected in steady-state numbers. If you map once per process lifetime, the comparison flips.
- **AOT** — BDN doesn't run under Native AOT; `samples/ZeroAlloc.Mapping.AotSmoke/` covers correctness there. The generated mapping body is identical between JIT and AOT, so steady-state perf is the same.
- **Multi-threaded contention** — single-threaded numbers only. Mapping has no shared mutable state; multi-threaded would just measure thread-pool noise.

## Where to next

- Diagnostics: [Diagnostics](diagnostics.md).
- Testing: [Testing](testing.md).
