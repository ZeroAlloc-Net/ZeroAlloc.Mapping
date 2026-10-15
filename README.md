# ZeroAlloc.Mapping

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Mapping.svg)](https://www.nuget.org/packages/ZeroAlloc.Mapping)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

ZeroAlloc.Mapping is a Roslyn source generator that emits strongly-typed Command→Domain→DTO mappers at compile time. No reflection. No expression trees. No runtime configuration. The success path runs with zero allocation beyond the destination instance itself, and the generated code is fully Native AOT compatible.

## Install

```bash
dotnet add package ZeroAlloc.Mapping
```

The generator runs automatically as part of `dotnet build` — there is no DI registration or runtime configuration.

## Example

```csharp
using ZeroAlloc.Mapping;

// 3. Use it
var order = AppMappings.Map(new OrderRequest(42, "rush"));

// 1. Define source and destination
public sealed record OrderRequest(int Id, string Notes);
public sealed record Order(int Id, string Notes);

// 2. Declare the mapper — generator fills in the partial
[Map<OrderRequest, Order>]
public static partial class AppMappings { }
```

What the generator emits is a null check and a single direct constructor call — no intermediate buffer, no boxing, no closure:

```csharp
public static Order Map(OrderRequest src)
{
    global::System.ArgumentNullException.ThrowIfNull(src);
    var __dst = new Order(
        Id: src.Id,
        Notes: src.Notes
    );
    return __dst;
}
```

## Performance

ZeroAlloc.Mapping matches Mapperly (the other source-generator mapper) on hot paths and is **1.8–14× faster than AutoMapper on single-object mappings** with allocation parity. On the 1000-item collection benchmark AutoMapper is faster but allocates 11% more. Benchmarks below run on .NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.8.

| Scenario | ZeroAlloc | Mapperly | AutoMapper | HandWritten |
|---|---:|---:|---:|---:|
| FlatIdentity (record copy) | 30.8 ns | 26.3 ns | 76.4 ns | 32.4 ns |
| FlatConversion (string→int) | 185.8 ns | 201.9 ns | 327.2 ns | 167.9 ns |
| Flattening (`a.b.c → d`) | 22.9 ns | 23.0 ns | 70.8 ns | 29.2 ns |
| Polymorphic dispatch | 11.4 ns | 11.0 ns | 48.8 ns | 11.3 ns |
| UpdateInPlace | 6.1 ns | 6.0 ns | **83.9 ns (~14× slower)** | 4.7 ns |
| TryMap (Result<T,Error>) | 208.4 ns | 195.4 ns (no native) | — | 222.6 ns |
| Collection (1000 items) | 38.6 µs | 41.1 µs | 30.4 µs | 39.6 µs |

Allocation: identical across mappers except `Collection` where AutoMapper allocates 11% more (86.5 KB vs 78.2 KB).

See [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/performance.md) for the full BenchmarkDotNet output, methodology, and the enforced per-call allocation budgets (CI fails on regression).

## Features

- **`[Map<TSrc, TDst>]`** — direct property/constructor mapping with case-sensitive matching and compile-time diagnostics on mismatch
- **`[TryMap<TSrc, TDst>]`** — fallible mapping returning `Result<TDst, MappingError>` (integrates with [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) smart constructors)
- **`[MapProperty]` flattening** — `"Customer.Address.City" → City` via dotted paths
- **`[BeforeMap]` / `[AfterMap]` hooks** — zero overhead when bodies are empty
- **`[PolymorphicMap]`** — type-switch dispatch generated at compile time
- **`[ReverseMap]`** — symmetric Domain↔DTO with a single declaration
- **Update-in-place** — declare `static partial void Map(TSrc src, TDst existingDst)` next to `[Map<,>]` and the generator writes into the existing instance; the whole call allocates nothing, enforced by an `AllocationGate` test
- **Collection overloads** — `List<T>`, `T[]`, `IEnumerable<T>`, `IReadOnlyList<T>` auto-emitted (opt-out via `[SkipCollectionOverloads]`)
- **`[MappingCulture]`** — culture-aware `decimal.Parse` / `DateTime.Parse` per-mapper
- **ValueObjects integration** — a smart constructor that throws comes back from `TryMap` as a failed `Result` whose `MappingError.Code` is `"mapping.constructor.threw"`
- **Native AOT** — no reflection, no `MakeGenericType`, no expression compilation; trim/AOT warnings treated as errors in the AOT smoke gate

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/getting-started.md) | Install + first mapper in five minutes |
| [Basic Mapping](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/basic-mapping.md) | Property matching, conversions, customisation |
| [Flattening](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/flattening.md) | Dotted source paths via `[MapProperty]` |
| [Collections](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/collections.md) | Auto-emitted overloads + nested elements |
| [Polymorphic](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/polymorphic.md) | Type-switch dispatch |
| [Reverse Mapping](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/reverse-mapping.md) | Symmetric Domain↔DTO |
| [Update In Place](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/update-in-place.md) | Allocation-free destination reuse |
| [Hooks](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/hooks.md) | `[BeforeMap]` / `[AfterMap]` |
| [Culture & Strict](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/culture-and-strict.md) | `[MappingCulture]` and strict mode |
| [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/performance.md) | Benchmarks, allocation budgets, AOT |
| [Diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/diagnostics.md) | All ZAMP* diagnostic IDs |
| [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/testing.md) | How to unit-test generated mappers |
| [Advanced](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/advanced.md) | Edge cases, generator internals |
| [IQueryable Projection](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/iqueryable-projection.md) | `[Map<,>(Projection = true)]` — EF Core expression trees |
| [Cycle-Safe Mapping](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/cycle-safe-mapping.md) | `[Map<,>(CycleSafe = true)]` — graphs with back-references |
| [Deep Clone](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/deep-clone.md) | `[Map<,>(DeepClone = true)]` — whole-graph deep clone |

## License

MIT. See [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/LICENSE).
