# ZeroAlloc.Mapping

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Mapping.svg)](https://www.nuget.org/packages/ZeroAlloc.Mapping)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

**Source-generated object mapper for .NET, an AutoMapper alternative that writes your mapping code at compile time: no reflection, no runtime configuration, NativeAOT-ready.**

## Why ZeroAlloc.Mapping

- **Allocates what hand-written code allocates.** A flat record copy allocates 72 B, the same as `new Dst(...)` by hand. Update-in-place allocates 0 B, asserted by a test that fails CI on any regression.
- **1.8× to 13.8× faster than AutoMapper** on single-object mappings, measured in the same BenchmarkDotNet run.
- **Works under NativeAOT and trimming.** The generated body is plain property reads and a constructor call. The AOT smoke test publishes with trim and AOT warnings treated as errors.
- **Mistakes fail the build, not the request.** A destination property with no source is compile error ZAMP001, not a runtime exception on first use.

## Install

```bash
dotnet add package ZeroAlloc.Mapping
```

The generator runs as part of `dotnet build`. There is no DI registration and no startup configuration.

## Quick start

```csharp
using ZeroAlloc.Mapping;

var order = OrderMappings.Map(new OrderRequest(42, "rush"));            // a new Order
var orders = OrderMappings.Map(new List<OrderRequest> { new(1, "a"), new(2, "b") });
OrderMappings.Map(new OrderRequest(42, "gift wrap"), order);            // update in place, 0 B

Console.WriteLine($"{order.Id}: {order.Notes}, {orders.Count} mapped");

public sealed record OrderRequest(int Id, string Notes);

public sealed class Order
{
    public int Id { get; set; }
    public string Notes { get; set; } = "";
}

[Map<OrderRequest, Order>]
public static partial class OrderMappings
{
    public static partial void Map(OrderRequest src, Order existingDst);
}
```

One attribute gives you the single-object mapper and the `List<T>`, `T[]`, `IEnumerable<T>` and `IReadOnlyList<T>` overloads. Declaring the partial `void Map(src, existing)` adds the update-in-place form. Each generated method is a null check and a direct constructor call or property writes.

## What you get

- **Constructor and property mapping** with conversions, renames and per-property customisation.
- **Fallible mapping**: `[TryMap<,>]` returns `Result<T, MappingError>` instead of throwing, and a throwing smart constructor from [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects) comes back as a failed result.
- **Flattening**: map `Customer.Address.City` onto `City` with `[MapProperty]`.
- **Reverse mapping**: one `[ReverseMap<,>]` declaration gives you both directions.
- **Polymorphic dispatch**: a compile-time type switch over derived types.
- **Update in place** for EF Core tracked entities and pooled objects.
- **Collection overloads** generated automatically, with an opt-out.
- **Before and after hooks**, which cost nothing when empty.
- **Culture-aware parsing** and strict or case-insensitive matching per mapper.
- **IQueryable projection** for EF Core, **cycle-safe mapping** for graphs with back-references, and **deep clone**.
- **Integration**: [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) powers the `Result<T, MappingError>` path.

## Performance

.NET 10, BenchmarkDotNet, same run. The last column is AutoMapper's time divided by ZeroAlloc.Mapping's.

| Scenario | ZeroAlloc.Mapping | AutoMapper | AutoMapper time |
|---|---:|---:|---:|
| Update in place | 0 B | 0 B | 13.8× slower |
| Polymorphic dispatch | 32 B | 32 B | 4.3× slower |
| 1,000-item collection | 78.18 KB | 86.52 KB | 0.79×, AutoMapper is faster |

Mapperly, the other source-generated mapper, allocates within 0.1 KB of ZeroAlloc.Mapping in every scenario and runs at 0.85× to 1.09× its time. The [performance page](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/performance.md) has all seven scenarios, the hand-written baseline, the per-call allocation budgets CI enforces, and how to reproduce the numbers.

## Learn more

- [Documentation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/README.md): guides, cookbook, the [diagnostics reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/diagnostics.md) and [testing generated mappers](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/docs/testing.md).
- Related packages: [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results), [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects), [ZeroAlloc.Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation).
- [Changelog](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/CHANGELOG.md). Issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/issues).
- MIT licensed. See [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/main/LICENSE).
