# ZeroAlloc.Mapping

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Mapping.svg)](https://www.nuget.org/packages/ZeroAlloc.Mapping)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
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

// 1. Define source and destination
public sealed record OrderRequest(int Id, string Notes);
public sealed record Order(int Id, string Notes);

// 2. Declare the mapper — generator fills in the partial
[Map<OrderRequest, Order>]
public static partial class AppMappings { }

// 3. Use it
var order = AppMappings.Map(new OrderRequest(42, "rush"));
```

What the generator emits is a single direct constructor call — no intermediate buffer, no boxing, no closure:

```csharp
public static Order Map(OrderRequest src)
    => new Order(Id: src.Id, Notes: src.Notes);
```

## Performance

ZeroAlloc.Mapping matches Mapperly (the other source-generator mapper) on hot paths and is **2–18× faster than AutoMapper** with allocation parity. Benchmarks below run on .NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.8.

| Scenario | ZeroAlloc | Mapperly | AutoMapper | HandWritten |
|---|---:|---:|---:|---:|
| FlatIdentity (record copy) | 30.8 ns | 26.3 ns | 76.4 ns | 32.4 ns |
| FlatConversion (string→int) | 185.8 ns | 201.9 ns | 327.2 ns | 167.9 ns |
| Flattening (`a.b.c → d`) | 22.9 ns | 23.0 ns | 70.8 ns | 29.2 ns |
| Polymorphic dispatch | 11.4 ns | 11.0 ns | 48.8 ns | 11.3 ns |
| UpdateInPlace | 6.1 ns | 6.0 ns | **83.9 ns (~18×)** | 4.7 ns |
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
- **Update-in-place** — `void Map(src, ref dst)` overloads, zero destination allocation
- **Collection overloads** — `List<T>`, `T[]`, `IEnumerable<T>`, `IReadOnlyList<T>` auto-emitted (opt-out via `[SkipCollectionOverloads]`)
- **`[MappingCulture]`** — culture-aware `decimal.Parse` / `DateTime.Parse` per-mapper
- **ValueObjects integration** — smart-constructor failures bubble through `TryMap` as `MappingError.SmartCtorRejected`
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
| [Diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/diagnostics.md) | All ZAMAP* diagnostic IDs |
| [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/testing.md) | How to unit-test generated mappers |
| [Advanced](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/advanced.md) | Edge cases, generator internals |

## License

MIT. See [LICENSE](LICENSE).
