# Benchmark Harness Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Stand up a `benchmarks/ZeroAlloc.Mapping.Benchmarks` BenchmarkDotNet project comparing ZeroAlloc.Mapping against Mapperly, AutoMapper, and a hand-written baseline across seven scenarios, then funnel results into `docs/performance.md` via a one-command refresh script.

**Architecture:** New net10.0 console project under `benchmarks/`, modelled on `samples/ZeroAlloc.Mapping.AotSmoke/`. Each benchmark class isolates one mapping shape and exposes 4 methods (one per mapper). BDN's default markdown exporter writes per-scenario tables; `tools/import-benchmarks.ps1` splices them into `performance.md` between sentinel comments. Raw `BenchmarkDotNet.Artifacts/` is gitignored.

**Tech Stack:** .NET 10 (matches `global.json` SDK 10.0.202), BenchmarkDotNet (latest stable), Riok.Mapperly (latest), AutoMapper (latest), xUnit not used (this is an Exe, not tests).

**Design source:** [2026-05-09-benchmark-harness-design.md](./2026-05-09-benchmark-harness-design.md)

---

## Pre-flight Notes

**Repo conventions (read before starting):**
- Solution file is `ZeroAlloc.Mapping.slnx` (XML-based slnx, not .sln). Add the new project under a `<Folder Name="/benchmarks/">` node.
- Existing samples use `OutputType=Exe`, `IsPackable=false`, project references with `OutputItemType="Analyzer"` for the generator.
- Top-level `Directory.Build.props` injects analyzers (Meziantou, Roslynator, ErrorProne, Hyperlinq, ZeroAlloc.Analyzers) into every project. Benchmark code will trip plenty of warnings — suppress with `<NoWarn>` rather than fighting them, the AOT sample's NoWarn list is a reasonable starting point: `MA0002;MA0004;MA0006;MA0047;MA0048;EPC12`.
- Today's date for any dated commit / artifact: 2026-05-09.

**Working directory:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mapping`. All paths below are relative to that.

**Branching:** Create a feature branch `feat/benchmark-harness` before Task 1. Conventional-commit style for each commit (`feat(bench): ...`, `chore(bench): ...`).

---

## Task 1: Scaffold the benchmark project

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Program.cs`
- Modify: `ZeroAlloc.Mapping.slnx` (add the new project under a `/benchmarks/` folder)

**Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>ZeroAlloc.Mapping.Benchmarks</RootNamespace>
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
    <NoWarn>$(NoWarn);MA0002;MA0004;MA0006;MA0047;MA0048;EPC12;CA1812;CA1822</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="*" />
    <PackageReference Include="Riok.Mapperly" Version="*" />
    <PackageReference Include="AutoMapper" Version="*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Mapping\ZeroAlloc.Mapping.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Mapping.Generator\ZeroAlloc.Mapping.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

> Replace `Version="*"` with concrete latest stable versions before committing. Use `dotnet list package --outdated` after restore to confirm.

**Step 2: Create `Program.cs` with the BDN switcher**

```csharp
using BenchmarkDotNet.Running;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
```

**Step 3: Register in `ZeroAlloc.Mapping.slnx`**

Add a new `<Folder Name="/benchmarks/">` block to the slnx after `/samples/`:

```xml
<Folder Name="/benchmarks/">
  <Project Path="benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj" />
</Folder>
```

**Step 4: Verify build**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
Expected: Build succeeded, 0 errors. Warnings about empty assembly are fine.

**Step 5: Verify BDN runs (empty list)**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --list flat`
Expected: BDN's "no benchmarks found" output, exit code 0.

**Step 6: Commit**

```bash
git add benchmarks/ ZeroAlloc.Mapping.slnx
git commit -m "chore(bench): scaffold ZeroAlloc.Mapping.Benchmarks project"
```

---

## Task 2: Add `.gitignore` rule for BDN artifacts

**Files:**
- Modify: `.gitignore` (root)

**Step 1: Append rule**

Add at the end of `.gitignore`:

```
# BenchmarkDotNet output (curated tables ship to docs/performance.md instead)
benchmarks/**/BenchmarkDotNet.Artifacts/
```

**Step 2: Commit**

```bash
git add .gitignore
git commit -m "chore(bench): gitignore BenchmarkDotNet.Artifacts"
```

---

## Task 3: Shared model fixtures

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Models/Flat.cs`
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Models/Conversion.cs`
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Models/Flatten.cs`
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Models/Polymorphic.cs`

These are dumb data shapes — every benchmark scenario uses the same source/dest types across all four mappers, so they live in one place.

**Step 1: `Models/Flat.cs`** (used by FlatIdentity, Collection, UpdateInPlace)

```csharp
namespace ZeroAlloc.Mapping.Benchmarks.Models;

public sealed record FlatSrc(
    int Id, string Name, string Email, int Age,
    bool Active, double Score, long Version, string Country);

public sealed record FlatDst(
    int Id, string Name, string Email, int Age,
    bool Active, double Score, long Version, string Country);

public sealed class FlatDstMutable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public bool Active { get; set; }
    public double Score { get; set; }
    public long Version { get; set; }
    public string Country { get; set; } = "";
}
```

**Step 2: `Models/Conversion.cs`** (used by FlatConversion, TryMap)

```csharp
namespace ZeroAlloc.Mapping.Benchmarks.Models;

public enum Status { Active, Inactive, Pending }

public readonly record struct OrderId(int Value);

public sealed record ConvSrc(string Id, string Status, int Count, string Created);
public sealed record ConvDst(OrderId Id, Status Status, long Count, DateTime Created);
```

**Step 3: `Models/Flatten.cs`**

```csharp
namespace ZeroAlloc.Mapping.Benchmarks.Models;

public sealed record Address(string Street, string City, string Zip);
public sealed record Customer(int Id, string Name, Address Address);
public sealed record OrderSrc(int OrderId, Customer Customer, decimal Total);

public sealed record OrderFlat(int OrderId, int CustomerId, string CustomerName,
    string Street, string City, string Zip, decimal Total);
```

**Step 4: `Models/Polymorphic.cs`**

```csharp
namespace ZeroAlloc.Mapping.Benchmarks.Models;

public abstract record Animal(string Name);
public sealed record Dog(string Name, string Breed) : Animal(Name);
public sealed record Cat(string Name, bool Indoor) : Animal(Name);
public sealed record Bird(string Name, double Wingspan) : Animal(Name);

public abstract record AnimalDto(string Name);
public sealed record DogDto(string Name, string Breed) : AnimalDto(Name);
public sealed record CatDto(string Name, bool Indoor) : AnimalDto(Name);
public sealed record BirdDto(string Name, double Wingspan) : AnimalDto(Name);
```

**Step 5: Build to confirm models compile**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
Expected: 0 errors.

**Step 6: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Models/
git commit -m "feat(bench): add shared model fixtures"
```

---

## Task 4: Hand-written baseline mappers

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/HandWritten.cs`

The simplest possible mapping — `new Dst(...)` inline. No method indirection beyond a static call. This is the "speed of light" the others get measured against.

**Step 1: `Mappers/HandWritten.cs`**

```csharp
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

public static class HandWritten
{
    public static FlatDst MapFlat(FlatSrc s) => new(
        s.Id, s.Name, s.Email, s.Age, s.Active, s.Score, s.Version, s.Country);

    public static ConvDst MapConv(ConvSrc s) => new(
        new OrderId(int.Parse(s.Id, System.Globalization.CultureInfo.InvariantCulture)),
        Enum.Parse<Status>(s.Status),
        s.Count,
        DateTime.Parse(s.Created, System.Globalization.CultureInfo.InvariantCulture));

    public static OrderFlat MapFlatten(OrderSrc s) => new(
        s.OrderId, s.Customer.Id, s.Customer.Name,
        s.Customer.Address.Street, s.Customer.Address.City, s.Customer.Address.Zip,
        s.Total);

    public static List<FlatDst> MapList(List<FlatSrc> src)
    {
        var dst = new List<FlatDst>(src.Count);
        for (var i = 0; i < src.Count; i++) dst.Add(MapFlat(src[i]));
        return dst;
    }

    public static AnimalDto MapAnimal(Animal a) => a switch
    {
        Dog d => new DogDto(d.Name, d.Breed),
        Cat c => new CatDto(c.Name, c.Indoor),
        Bird b => new BirdDto(b.Name, b.Wingspan),
        _ => throw new NotSupportedException()
    };

    public static void UpdateInPlace(FlatSrc s, FlatDstMutable d)
    {
        d.Id = s.Id; d.Name = s.Name; d.Email = s.Email; d.Age = s.Age;
        d.Active = s.Active; d.Score = s.Score; d.Version = s.Version; d.Country = s.Country;
    }
}
```

**Step 2: Build to confirm**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
Expected: 0 errors.

**Step 3: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/HandWritten.cs
git commit -m "feat(bench): add hand-written baseline mappers"
```

---

## Task 5: ZeroAlloc.Mapping mappers

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/ZaMappers.cs`

**Step 1: `Mappers/ZaMappers.cs`** — one `[Map]` per scenario shape

```csharp
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

[Map<FlatSrc, FlatDst>]
public static partial class ZaFlat { }

[Map<ConvSrc, ConvDst>]
public static partial class ZaConv { }

[Map<OrderSrc, OrderFlat>]
public static partial class ZaFlatten
{
    [MapProperty("Customer.Id", "CustomerId")]
    [MapProperty("Customer.Name", "CustomerName")]
    [MapProperty("Customer.Address.Street", "Street")]
    [MapProperty("Customer.Address.City", "City")]
    [MapProperty("Customer.Address.Zip", "Zip")]
    public static partial OrderFlat Map(OrderSrc src);
}

[PolymorphicMap<Animal, AnimalDto>(typeof(Dog), typeof(DogDto))]
[PolymorphicMap<Animal, AnimalDto>(typeof(Cat), typeof(CatDto))]
[PolymorphicMap<Animal, AnimalDto>(typeof(Bird), typeof(BirdDto))]
public static partial class ZaPoly { }

[Map<FlatSrc, FlatDstMutable>]
public static partial class ZaUpdate
{
    public static partial void Map(FlatSrc src, FlatDstMutable dst);
}

[TryMap<ConvSrc, ConvDst>]
public static partial class ZaTry { }
```

> Verify the `[PolymorphicMap]` attribute shape against the actual generator surface in `src/ZeroAlloc.Mapping/PolymorphicMapAttribute.cs` — it may take a different argument order or use a different attribute style. Adjust if so. Same caveat for `[TryMap]` — confirm against `src/ZeroAlloc.Mapping/TryMapAttribute.cs`.

**Step 2: Build**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
Expected: 0 errors. The generator should emit `Map` methods on each partial class.

**Step 3: Spot-check the generated code**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj /p:EmitCompilerGeneratedFiles=true /p:CompilerGeneratedFilesOutputPath=generated`
Expected: `benchmarks/ZeroAlloc.Mapping.Benchmarks/generated/` contains the emitted partial class bodies. Skim `ZaFlat.g.cs` to confirm a single `new FlatDst(src.Id, ...)` body. Do NOT commit the generated folder.

**Step 4: Add generated folder to .gitignore**

Append to `.gitignore`:

```
benchmarks/**/generated/
```

**Step 5: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/ZaMappers.cs .gitignore
git commit -m "feat(bench): add ZeroAlloc.Mapping mapper definitions"
```

---

## Task 6: Mapperly mappers

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/MapperlyMappers.cs`

**Step 1: `Mappers/MapperlyMappers.cs`**

```csharp
using Riok.Mapperly.Abstractions;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

[Mapper]
public static partial class MapperlyFlat
{
    public static partial FlatDst Map(FlatSrc src);
}

[Mapper]
public static partial class MapperlyConv
{
    [MapProperty(nameof(ConvSrc.Id), nameof(ConvDst.Id))]
    public static partial ConvDst Map(ConvSrc src);
    private static OrderId IntToOrderId(string s) =>
        new(int.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
}

[Mapper]
public static partial class MapperlyFlatten
{
    [MapProperty(new[] { nameof(OrderSrc.Customer), nameof(Customer.Id) }, nameof(OrderFlat.CustomerId))]
    [MapProperty(new[] { nameof(OrderSrc.Customer), nameof(Customer.Name) }, nameof(OrderFlat.CustomerName))]
    [MapProperty(new[] { nameof(OrderSrc.Customer), nameof(Customer.Address), nameof(Address.Street) }, nameof(OrderFlat.Street))]
    [MapProperty(new[] { nameof(OrderSrc.Customer), nameof(Customer.Address), nameof(Address.City) }, nameof(OrderFlat.City))]
    [MapProperty(new[] { nameof(OrderSrc.Customer), nameof(Customer.Address), nameof(Address.Zip) }, nameof(OrderFlat.Zip))]
    public static partial OrderFlat Map(OrderSrc src);
}

[Mapper(IncludeDerivedTypes = true)]
public static partial class MapperlyPoly
{
    public static partial AnimalDto Map(Animal src);
    private static partial DogDto MapDog(Dog src);
    private static partial CatDto MapCat(Cat src);
    private static partial BirdDto MapBird(Bird src);
}

[Mapper]
public static partial class MapperlyUpdate
{
    public static partial void Update(FlatSrc src, FlatDstMutable dst);
}
```

> Mapperly's polymorphic API may differ — check the latest Mapperly docs (`riok.github.io/mapperly`) and adjust if the `IncludeDerivedTypes` shape is wrong. Same for the dotted-path syntax in flatten — Mapperly uses `string[]` for member chains, ZA uses `"a.b.c"` strings.

**Step 2: Build**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
Expected: 0 errors. Mapperly's source generator emits the partial method bodies.

**Step 3: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/MapperlyMappers.cs
git commit -m "feat(bench): add Mapperly mapper definitions"
```

---

## Task 7: AutoMapper profile

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/AutoMapperProfile.cs`

**Step 1: `Mappers/AutoMapperProfile.cs`**

```csharp
using AutoMapper;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

public sealed class BenchmarkProfile : Profile
{
    public BenchmarkProfile()
    {
        CreateMap<FlatSrc, FlatDst>();
        CreateMap<FlatSrc, FlatDstMutable>();

        CreateMap<ConvSrc, ConvDst>()
            .ForMember(d => d.Id, o => o.MapFrom(s =>
                new OrderId(int.Parse(s.Id, System.Globalization.CultureInfo.InvariantCulture))))
            .ForMember(d => d.Status, o => o.MapFrom(s => Enum.Parse<Status>(s.Status)))
            .ForMember(d => d.Created, o => o.MapFrom(s =>
                DateTime.Parse(s.Created, System.Globalization.CultureInfo.InvariantCulture)));

        CreateMap<OrderSrc, OrderFlat>()
            .ForMember(d => d.CustomerId, o => o.MapFrom(s => s.Customer.Id))
            .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer.Name))
            .ForMember(d => d.Street, o => o.MapFrom(s => s.Customer.Address.Street))
            .ForMember(d => d.City, o => o.MapFrom(s => s.Customer.Address.City))
            .ForMember(d => d.Zip, o => o.MapFrom(s => s.Customer.Address.Zip));

        CreateMap<Animal, AnimalDto>()
            .Include<Dog, DogDto>()
            .Include<Cat, CatDto>()
            .Include<Bird, BirdDto>();
        CreateMap<Dog, DogDto>();
        CreateMap<Cat, CatDto>();
        CreateMap<Bird, BirdDto>();
    }
}

public static class AutoMapperFactory
{
    public static IMapper Build()
    {
        var config = new MapperConfiguration(c => c.AddProfile<BenchmarkProfile>());
        return config.CreateMapper();
    }
}
```

**Step 2: Build**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
Expected: 0 errors.

**Step 3: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Mappers/AutoMapperProfile.cs
git commit -m "feat(bench): add AutoMapper profile"
```

---

## Task 8: Sanity check — all four mappers produce structurally identical output

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Sanity.cs`

We don't ship xUnit tests in this project, but we do want a smoke check that all four mappers produce the same destination so the benchmark numbers are honestly comparing the same work. This runs once at startup before BDN.

**Step 1: `Sanity.cs`**

```csharp
using AutoMapper;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks;

internal static class Sanity
{
    public static void AssertParity()
    {
        var am = AutoMapperFactory.Build();
        var src = new FlatSrc(1, "n", "e", 30, true, 1.5, 7, "NL");
        var hand = HandWritten.MapFlat(src);
        var za = ZaFlat.Map(src);
        var mp = MapperlyFlat.Map(src);
        var au = am.Map<FlatDst>(src);
        if (!(hand == za && hand == mp && hand == au))
            throw new InvalidOperationException("Flat parity mismatch.");

        // Add similar parity checks for Conv, Flatten, Poly, UpdateInPlace if desired.
    }
}
```

**Step 2: Wire into `Program.cs`**

Modify `Program.cs`:

```csharp
using BenchmarkDotNet.Running;
using ZeroAlloc.Mapping.Benchmarks;

Sanity.AssertParity();

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
```

**Step 3: Run sanity (no benchmarks needed)**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --list flat`
Expected: No exception thrown by `AssertParity`. BDN reports no benchmarks. Exit 0.

**Step 4: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Sanity.cs benchmarks/ZeroAlloc.Mapping.Benchmarks/Program.cs
git commit -m "feat(bench): parity check across all four mappers"
```

---

## Task 9: First benchmark — FlatIdentity

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/FlatIdentityBench.cs`

This is the template all other scenarios follow. Get it right once, copy-paste-edit for the rest.

**Step 1: `Scenarios/FlatIdentityBench.cs`**

```csharp
using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FlatIdentityBench
{
    private FlatSrc _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new FlatSrc(42, "Marcel", "m@example.com", 30, true, 99.5, 7, "NL");
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public FlatDst HandWritten_() => HandWritten.MapFlat(_src);
    [Benchmark] public FlatDst ZeroAlloc_() => ZaFlat.Map(_src);
    [Benchmark] public FlatDst Mapperly_() => MapperlyFlat.Map(_src);
    [Benchmark] public FlatDst AutoMapper_() => _autoMapper.Map<FlatDst>(_src);
}
```

**Step 2: Build**

Run: `dotnet build benchmarks/ZeroAlloc.Mapping.Benchmarks/ZeroAlloc.Mapping.Benchmarks.csproj`
Expected: 0 errors.

**Step 3: Run just this scenario in dry mode (~3 seconds)**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*FlatIdentityBench*" --job dry`
Expected: 4 rows in BDN's summary table — HandWritten_ (Baseline), ZeroAlloc_, Mapperly_, AutoMapper_. All Mean values populated, all "Allocated" columns populated.

**Step 4: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/FlatIdentityBench.cs
git commit -m "feat(bench): FlatIdentity scenario"
```

---

## Task 10: FlatConversion benchmark

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/FlatConversionBench.cs`

**Step 1: Copy `FlatIdentityBench.cs` → `FlatConversionBench.cs`** and adapt:

```csharp
using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FlatConversionBench
{
    private ConvSrc _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new ConvSrc("42", "Active", 100, "2026-05-09T12:00:00Z");
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public ConvDst HandWritten_() => HandWritten.MapConv(_src);
    [Benchmark] public ConvDst ZeroAlloc_() => ZaConv.Map(_src);
    [Benchmark] public ConvDst Mapperly_() => MapperlyConv.Map(_src);
    [Benchmark] public ConvDst AutoMapper_() => _autoMapper.Map<ConvDst>(_src);
}
```

**Step 2: Build + dry run**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*FlatConversionBench*" --job dry`
Expected: 4 rows, all populated.

**Step 3: Commit**

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/FlatConversionBench.cs
git commit -m "feat(bench): FlatConversion scenario"
```

---

## Task 11: Flattening benchmark

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/FlatteningBench.cs`

**Step 1: Implement**

```csharp
using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FlatteningBench
{
    private OrderSrc _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new OrderSrc(
            OrderId: 7,
            Customer: new Customer(42, "Marcel", new Address("Main 1", "Amsterdam", "1011AA")),
            Total: 99.99m);
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public OrderFlat HandWritten_() => HandWritten.MapFlatten(_src);
    [Benchmark] public OrderFlat ZeroAlloc_() => ZaFlatten.Map(_src);
    [Benchmark] public OrderFlat Mapperly_() => MapperlyFlatten.Map(_src);
    [Benchmark] public OrderFlat AutoMapper_() => _autoMapper.Map<OrderFlat>(_src);
}
```

**Step 2: Build + dry run + commit**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*FlatteningBench*" --job dry`
Expected: 4 rows.

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/FlatteningBench.cs
git commit -m "feat(bench): Flattening scenario"
```

---

## Task 12: Collection benchmark

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/CollectionBench.cs`

**Step 1: Implement** (1000 elements; mappers should expose List overloads)

```csharp
using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class CollectionBench
{
    private List<FlatSrc> _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = Enumerable.Range(0, 1000)
            .Select(i => new FlatSrc(i, "n", "e", i, true, i * 1.5, i, "NL"))
            .ToList();
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public List<FlatDst> HandWritten_() => HandWritten.MapList(_src);
    [Benchmark] public List<FlatDst> ZeroAlloc_() => ZaFlat.Map(_src);     // generated List<T> overload
    [Benchmark] public List<FlatDst> Mapperly_() => _src.Select(MapperlyFlat.Map).ToList();
    [Benchmark] public List<FlatDst> AutoMapper_() => _autoMapper.Map<List<FlatDst>>(_src);
}
```

> **Mapperly note:** Mapperly auto-generates collection overloads for `List<T> → List<T>` if you declare a `partial List<FlatDst> Map(List<FlatSrc>)`. If you'd rather measure that path, add the partial method to `MapperlyMappers.cs` in this task and call it directly. The `Select.ToList` form above measures the LINQ-based fallback instead — pick one and document the choice in `performance.md`.

**Step 2: Build + dry run + commit**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*CollectionBench*" --job dry`
Expected: 4 rows.

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/CollectionBench.cs
git commit -m "feat(bench): Collection scenario"
```

---

## Task 13: Polymorphic benchmark

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/PolymorphicBench.cs`

**Step 1: Implement**

```csharp
using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class PolymorphicBench
{
    private Animal _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new Dog("Rex", "Labrador");  // exercise the Dog branch
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public AnimalDto HandWritten_() => HandWritten.MapAnimal(_src);
    [Benchmark] public AnimalDto ZeroAlloc_() => ZaPoly.Map(_src);
    [Benchmark] public AnimalDto Mapperly_() => MapperlyPoly.Map(_src);
    [Benchmark] public AnimalDto AutoMapper_() => _autoMapper.Map<AnimalDto>(_src);
}
```

**Step 2: Build + dry run + commit**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*PolymorphicBench*" --job dry`
Expected: 4 rows.

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/PolymorphicBench.cs
git commit -m "feat(bench): Polymorphic scenario"
```

---

## Task 14: Update-in-place benchmark

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/UpdateInPlaceBench.cs`

**Step 1: Implement** — note the destination is reused across iterations (that's the whole point of update-in-place)

```csharp
using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class UpdateInPlaceBench
{
    private FlatSrc _src = null!;
    private FlatDstMutable _dst = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new FlatSrc(42, "Marcel", "m@example.com", 30, true, 99.5, 7, "NL");
        _dst = new FlatDstMutable();
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public void HandWritten_() => HandWritten.UpdateInPlace(_src, _dst);
    [Benchmark] public void ZeroAlloc_() => ZaUpdate.Map(_src, _dst);
    [Benchmark] public void Mapperly_() => MapperlyUpdate.Update(_src, _dst);
    [Benchmark] public void AutoMapper_() => _autoMapper.Map(_src, _dst);
}
```

**Step 2: Build + dry run + commit**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*UpdateInPlaceBench*" --job dry`
Expected: 4 rows. The HandWritten + ZeroAlloc + Mapperly rows should report `0 B` allocated (no destination allocation).

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/UpdateInPlaceBench.cs
git commit -m "feat(bench): UpdateInPlace scenario"
```

---

## Task 15: TryMap benchmark

**Files:**
- Create: `benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/TryMapBench.cs`

**Step 1: Implement** — AutoMapper omitted (no `Result<T, Error>` equivalent)

```csharp
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class TryMapBench
{
    private ConvSrc _src = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new ConvSrc("42", "Active", 100, "2026-05-09T12:00:00Z");
    }

    [Benchmark(Baseline = true)] public ConvDst HandWritten_() => HandWritten.MapConv(_src);
    [Benchmark] public Result<ConvDst, MappingError> ZeroAlloc_() => ZaTry.TryMap(_src);
    // Mapperly has no native Result-type — wrap in try/catch for fairness:
    [Benchmark]
    public ConvDst Mapperly_()
    {
        try { return MapperlyConv.Map(_src); }
        catch { throw; }
    }
}
```

> Confirm the `Result<T, MappingError>` namespace — it's likely `ZeroAlloc.Results` per the broader workspace, but might be re-exported from `ZeroAlloc.Mapping`. Adjust the using if needed.

**Step 2: Build + dry run + commit**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*TryMapBench*" --job dry`
Expected: 3 rows.

```bash
git add benchmarks/ZeroAlloc.Mapping.Benchmarks/Scenarios/TryMapBench.cs
git commit -m "feat(bench): TryMap scenario"
```

---

## Task 16: Full run — capture real numbers

**Files:** none modified

**Step 1: Run all scenarios with default (real) job**

Run: `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*"`
Expected: ~10–20 minutes total. Output written to `benchmarks/ZeroAlloc.Mapping.Benchmarks/BenchmarkDotNet.Artifacts/results/*.md`.

**Step 2: Sanity-review the numbers**

Open each `*-report-github.md` file. Check:
- HandWritten and ZeroAlloc should have `0 B` allocated for FlatIdentity, UpdateInPlace.
- Mapperly should be within ~30% of ZeroAlloc on Mean for most scenarios.
- AutoMapper should be 10x–100x slower with non-zero allocation everywhere.

If any of those is wildly off, stop and investigate before publishing — likely a wiring bug in one of the mappers.

**Step 3: Don't commit yet** — the artifacts are gitignored. The next task ingests them into `performance.md`.

---

## Task 17: `import-benchmarks.ps1` script

**Files:**
- Create: `tools/import-benchmarks.ps1`

**Step 1: Write the PowerShell**

```powershell
#requires -Version 7
<#
.SYNOPSIS
  Splices BenchmarkDotNet result tables into docs/performance.md.

.DESCRIPTION
  Reads every *-report-github.md under
  benchmarks/ZeroAlloc.Mapping.Benchmarks/BenchmarkDotNet.Artifacts/results/
  and concatenates them into a single block, sandwiched between the
  <!-- BENCH:START --> and <!-- BENCH:END --> sentinels in performance.md.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$resultsDir = Join-Path $repoRoot 'benchmarks/ZeroAlloc.Mapping.Benchmarks/BenchmarkDotNet.Artifacts/results'
$perfMd = Join-Path $repoRoot 'docs/performance.md'

if (-not (Test-Path $resultsDir)) {
    throw "No results at $resultsDir. Run 'dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter \"*\"' first."
}

$reports = Get-ChildItem -Path $resultsDir -Filter '*-report-github.md' | Sort-Object Name
if ($reports.Count -eq 0) { throw "No -report-github.md files found." }

$blocks = foreach ($r in $reports) {
    $title = ($r.BaseName -replace '\.Scenarios\.', '' -replace '-report-github$', '')
    $body = Get-Content $r.FullName -Raw
    "### $title`n`n$body`n"
}

$content = $blocks -join "`n"
$timestamp = (Get-Date).ToString('yyyy-MM-dd')
$wrapped = "<!-- BENCH:START -->`n_Last refreshed: $timestamp_`n`n$content`n<!-- BENCH:END -->"

$md = Get-Content $perfMd -Raw
$pattern = '<!-- BENCH:START -->[\s\S]*?<!-- BENCH:END -->'
if ($md -notmatch $pattern) {
    throw "Sentinels not found in $perfMd. Add '<!-- BENCH:START -->' and '<!-- BENCH:END -->' before running."
}
$updated = [regex]::Replace($md, $pattern, [System.Text.RegularExpressions.Regex]::Escape($wrapped) -replace '\\(.)', '$1')
# regex Escape + un-escape dance avoids Replace's $-substitution biting us on $ in the table content.
Set-Content -Path $perfMd -Value $updated -NoNewline

Write-Host "Imported $($reports.Count) benchmark reports into $perfMd."
```

> The `[regex]::Replace` + escape dance is awkward — an alternative is `[regex]::Replace($md, $pattern, { param($m) $wrapped })` using a MatchEvaluator delegate, which sidesteps `$`-substitution entirely. Use whichever you prefer.

**Step 2: Test the script (will fail — sentinels missing — that's expected)**

Run: `pwsh tools/import-benchmarks.ps1`
Expected: Throws "Sentinels not found in ...". Good — confirms it's reading the right file.

**Step 3: Commit**

```bash
git add tools/import-benchmarks.ps1
git commit -m "feat(bench): import-benchmarks.ps1 splices BDN results into performance.md"
```

---

## Task 18: Add sentinels + benchmark prose to `performance.md`

**Files:**
- Modify: `docs/performance.md`

**Step 1: Replace the "Comparison with reflection-based mappers" section's closing line**

Currently `performance.md:96` ends with:

> Mapperly is the closest peer — also a source generator, also zero-reflection. Detailed BenchmarkDotNet comparisons against Mapperly and AutoMapper are deferred to a separate document; this page documents budgets, not benchmarks.

Replace the second sentence ("Detailed BenchmarkDotNet ...") with a forward-link:

```markdown
Mapperly is the closest peer — also a source generator, also zero-reflection. Concrete BenchmarkDotNet numbers comparing all four (ZeroAlloc.Mapping, Mapperly, AutoMapper, hand-written) are below.
```

**Step 2: Insert a new "Benchmarks" section before "Where to next"**

Between the existing closing of "Comparison with reflection-based mappers" and "## Where to next", insert:

```markdown
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
```

**Step 3: Verify the sentinels match what the script expects**

Run: `pwsh tools/import-benchmarks.ps1`
Expected: This time it should NOT throw "Sentinels not found". It will throw "No results at ..." if you haven't run benchmarks yet — that's fine, the next task fixes that.

**Step 4: Commit**

```bash
git add docs/performance.md
git commit -m "docs(performance): add Benchmarks section with sentinels"
```

---

## Task 19: First real import — benchmark numbers into the docs

**Files:**
- Modify: `docs/performance.md` (script-driven)

**Step 1: Ensure benchmarks have been run** (Task 16 output should still be on disk)

Run: `ls benchmarks/ZeroAlloc.Mapping.Benchmarks/BenchmarkDotNet.Artifacts/results/*.md`
Expected: 7 `*-report-github.md` files. If missing, re-run Task 16's command.

**Step 2: Run the import script**

Run: `pwsh tools/import-benchmarks.ps1`
Expected: "Imported 7 benchmark reports into .../docs/performance.md."

**Step 3: Eyeball the diff**

Run: `git diff docs/performance.md`
Expected: The `<!-- BENCH:START --> ... <!-- BENCH:END -->` block now contains 7 markdown sub-sections, one per scenario, each with a results table.

**Step 4: Commit**

```bash
git add docs/performance.md
git commit -m "docs(performance): import first benchmark run"
```

---

## Task 20: README + CHANGELOG entries

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `README.md` (optional — only if there's a "Performance" or "Benchmarks" section to update)

**Step 1: Append to CHANGELOG.md** (under "Unreleased" or the next minor version section)

```markdown
### Added
- Benchmark harness at `benchmarks/ZeroAlloc.Mapping.Benchmarks/` comparing
  ZeroAlloc.Mapping vs Mapperly vs AutoMapper vs hand-written across 7 scenarios.
  Numbers feed `docs/performance.md` via `tools/import-benchmarks.ps1`.
```

**Step 2: Check README.md for a perf-related blurb that should now link to the benchmarks section**

If the README has a "Performance" line or table, append a link to the benchmarks section. If not, skip.

**Step 3: Commit**

```bash
git add CHANGELOG.md README.md
git commit -m "docs: announce benchmark harness in CHANGELOG"
```

---

## Task 21: Open PR

**Files:** none

**Step 1: Push branch**

Run: `git push -u origin feat/benchmark-harness`

**Step 2: Open PR via gh**

```bash
gh pr create --title "Add BenchmarkDotNet harness comparing ZA.Mapping / Mapperly / AutoMapper / hand-written" --body "$(cat <<'EOF'
## Summary
- New `benchmarks/ZeroAlloc.Mapping.Benchmarks/` project (net10.0, Exe)
- 7 scenarios × 4 mappers (TryMap is 3 — AutoMapper has no Result equivalent)
- `tools/import-benchmarks.ps1` splices BDN markdown into `docs/performance.md`
- First real numbers committed in `docs/performance.md`

## Test plan
- [ ] `dotnet build` clean
- [ ] `dotnet run -c Release --project benchmarks/... -- --filter "*FlatIdentityBench*" --job dry` — 4 rows
- [ ] `pwsh tools/import-benchmarks.ps1` — splices without error
- [ ] `docs/performance.md` renders correctly on the docs site preview

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 3: Done.** Wait for review, address feedback, merge.

---

## Verification checklist (run before opening PR)

- [ ] `dotnet build ZeroAlloc.Mapping.slnx` — 0 errors, 0 warnings (analyzer noise excepted)
- [ ] `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --list flat` — lists 7 benchmark classes
- [ ] `dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter "*" --job dry` — every benchmark runs without exception
- [ ] `pwsh tools/import-benchmarks.ps1` — splices results without error
- [ ] `docs/performance.md` between sentinels contains 7 result tables
- [ ] `git status` — clean (artifacts gitignored, no stray files)
- [ ] `git log --oneline feat/benchmark-harness ^main` — ~12 atomic conventional commits

---

## Reference

- **Design doc**: [2026-05-09-benchmark-harness-design.md](./2026-05-09-benchmark-harness-design.md)
- **AOT sample (csproj reference shape)**: [samples/ZeroAlloc.Mapping.AotSmoke/ZeroAlloc.Mapping.AotSmoke.csproj](../../samples/ZeroAlloc.Mapping.AotSmoke/ZeroAlloc.Mapping.AotSmoke.csproj)
- **Existing performance page**: [docs/performance.md](../performance.md)
- **slnx**: [ZeroAlloc.Mapping.slnx](../../ZeroAlloc.Mapping.slnx)
