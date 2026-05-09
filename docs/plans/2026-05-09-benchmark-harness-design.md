---
id: benchmark-harness-design
title: Benchmark Harness Design (2026-05-09)
description: BenchmarkDotNet comparison ZeroAlloc.Mapping vs Mapperly vs AutoMapper vs hand-written, feeding performance.md.
---

# Benchmark Harness Design — 2026-05-09

## Goal

Produce reproducible, defensible numbers for `docs/performance.md` that compare:

- **ZeroAlloc.Mapping** (source-generated, this package)
- **Mapperly** (source-generated, closest competitor)
- **AutoMapper** (reflection-based, the incumbent everyone migrates from)
- **Hand-written** (`new Dst(src.A, src.B, ...)` — the absolute floor)

The 4-bar chart is the entire pitch: ZA should tie hand-written, Mapperly should be near-identical, AutoMapper should be 1-2 orders of magnitude slower with non-zero allocation. If those expectations don't hold, that's a finding we want to surface, not hide.

## Project layout

```
ZeroAlloc.Mapping/
  benchmarks/
    ZeroAlloc.Mapping.Benchmarks/
      ZeroAlloc.Mapping.Benchmarks.csproj  (net10.0, Exe)
      Program.cs                            (BenchmarkSwitcher.FromAssembly)
      Scenarios/
        FlatIdentityBench.cs
        FlatConversionBench.cs
        FlatteningBench.cs
        CollectionBench.cs
        PolymorphicBench.cs
        UpdateInPlaceBench.cs
        TryMapBench.cs
      Mappers/
        ZaMappers.cs        ([Map<>] partial classes)
        MapperlyMappers.cs  ([Mapper] partial classes)
        AutoMapperProfile.cs
        HandWritten.cs
      Models/
        (shared fixture types)
```

Project lives under a new `benchmarks/` folder alongside `tests/`. Not added to the `dotnet pack` graph — benchmarks are a dev-only artifact.

## Scenarios

Seven benchmarks, each runs all four mappers (TryMap drops AutoMapper since it has no equivalent).

| # | Scenario | What it isolates |
|---|---|---|
| 1 | FlatIdentity | 8-prop record, identity types. The absolute baseline; ZA should tie hand-written. |
| 2 | FlatConversion | `string→int.Parse`, `string→enum`, `int→long` cast, single-arg ctor wrap. Per-conversion overhead. |
| 3 | Flattening | `Customer.Address.City` style. Nested member-walk overhead. |
| 4 | Collection | `List<Src>(1000) → List<Dst>`. Loop dispatch amortised over 1000 elements. |
| 5 | Polymorphic | 3-type hierarchy, runtime dispatch. ZA `[PolymorphicMap]` vs AutoMapper `Include<>` vs Mapperly `[UseMapper]`. |
| 6 | UpdateInPlace | `void Map(src, existingDst)`. ZA + Mapperly native; AutoMapper via `mapper.Map(src, dst)`; hand-written = direct setter assignment. |
| 7 | TryMap | Fallible mapping. ZA `Result<T, MappingError>`, hand-written, Mapperly. AutoMapper omitted (no equivalent). |

## Metrics

- `[MemoryDiagnoser]` — **allocated bytes/op is the headline number**.
- Mean time, std-dev, gen0/1/2 collections.
- No `[ThreadingDiagnoser]` — single-threaded is the honest comparison (mapping is per-request, not contended).

## Runtime targets

- `net10.0` JIT, default `[SimpleJob]` config (warmup + ~15 measurement iterations). Repo's `global.json` pins SDK 10.0.x; matching the production target keeps numbers honest.
- **No** BDN AOT job — BDN's AOT support is brittle. AOT correctness is already covered by `tests/ZeroAlloc.Mapping.AotSmokeTests/`; `performance.md` will note "AOT publish doesn't change the generated mapping body — same IL — so steady-state perf is identical to JIT."

## Fairness rules (documented in `performance.md`)

- All four mappers consume the same source instance and produce structurally identical destinations.
- AutoMapper `IMapper` built in `[GlobalSetup]` — profile compilation excluded from per-iteration cost. Startup tax is real but distinct from steady-state cost; `performance.md` calls it out separately with a one-shot measurement.
- Mapperly + ZA partial classes generated at compile time — zero setup cost.
- Hand-written = inline `new Dst(...)`, no helper method indirection.
- All scenarios use `record class` source/destination types unless the scenario specifically tests something else (e.g. polymorphic uses sealed inheritance hierarchy).

## Output flow into `docs/performance.md`

1. BDN exports markdown by default to `BenchmarkDotNet.Artifacts/results/*.md`.
2. `tools/import-benchmarks.ps1` copies the markdown tables into the `## Results` section of `docs/performance.md`, replacing whatever's between `<!-- BENCH:START -->` / `<!-- BENCH:END -->` markers.
3. Raw artifacts gitignored (`benchmarks/**/BenchmarkDotNet.Artifacts/`). Only curated tables ship to the docs site.
4. Re-runnable in three commands:
   ```
   dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks
   pwsh tools/import-benchmarks.ps1
   git commit -am "Refresh benchmark numbers"
   ```

## `performance.md` final shape

- **Methodology** — hardware spec line, runtime, scenario definitions, fairness rules.
- **Results** — one 4-row table per scenario: Mapper / Mean / Allocated / Ratio-vs-handwritten.
- **AOT note** — generated body is identical; AOT changes startup, not steady state.
- **Reproduction** — the three-line command above.

## Effort estimate

| Work | Hours |
|---|---|
| Project scaffold + shared fixtures + 4 mapper wirings | 3 |
| 7 scenarios × 4 mapper wirings | 3 |
| `import-benchmarks.ps1` + `performance.md` prose | 1 |
| First run, sanity-check numbers, polish copy | 1 |
| **Total** | **~1 day** |

## Out of scope

- Multi-threaded contention benchmarks — no shared mutable state, would only measure thread-pool noise.
- Profile-guided JIT scenarios — `DOTNET_TieredPGO=1` is on by default in net8+, no toggle needed.
- Memory-traffic deep dive (perfview / dotMemory integration) — belongs in a separate doc if anyone asks for it.
- Comparison vs Mapster / TinyMapper / ExpressMapper — Mapperly + AutoMapper bracket the design space; adding more bars dilutes the chart.

## Risks

- **AutoMapper numbers may look bad enough that readers think we're cherry-picking.** Mitigate: link AutoMapper's own published benchmarks for cross-reference, document the exact configuration used, ship the harness so anyone can re-run.
- **Mapperly may match ZA exactly.** That's fine — the point of the page isn't "we're faster than Mapperly", it's "we're in the same league as Mapperly while having a different feature set (Result-types, Update-in-Place, polymorphic dispatch)". `performance.md` will frame it that way.
- **Numbers drift with .NET version updates.** `import-benchmarks.ps1` is one-command refresh; expect to re-run on each major .NET bump.

## Next step

Invoke `superpowers:writing-plans` skill to convert this design into a step-by-step implementation plan with file-level tasks.
