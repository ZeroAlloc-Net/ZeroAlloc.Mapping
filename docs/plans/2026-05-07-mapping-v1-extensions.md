# ZeroAlloc.Mapping v1 Extensions Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add 6 backlog items (B10, B13, B14, B1, B4, B12) to PR #1 of ZeroAlloc.Mapping before v1 ships, all additive.

**Architecture:** Each item extends existing v1 components — `PropertyMatcher`, `MapperDiscovery`, `MapEmitter`, `TryMapEmitter`, `Diagnostics`. Five new opt-in attributes (`BeforeMap`, `AfterMap`, `ReverseMap<,>`, `CaseInsensitiveMapping`, `StrictSourceMapping`). Three new diagnostics (ZAMP009/010/011). All landing on `feat/v1-scaffold-and-runtime`.

**Tech Stack:** .NET 8/9/10 multi-target, Roslyn `Microsoft.CodeAnalysis.CSharp` 4.14, xUnit 2, Verify 28.

**Design doc:** [`2026-05-07-mapping-v1-extensions-design.md`](2026-05-07-mapping-v1-extensions-design.md)

**Working repo:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mapping/` (already on `feat/v1-scaffold-and-runtime`).

---

## Implementation order — rationale

Chosen to minimise rework: simple matcher extensions first (each one teaches the next), then emitter changes, then the desugaring feature that depends on everything.

1. B10 (Obsolete skip) — 1 step in `PropertyMatcher`.
2. B13 (Case-insensitive) — same matcher extension point.
3. B14 (Strict source) — pure new diagnostic.
4. B1 (Flattening) — emitter changes (dotted-path expression).
5. B4 (Before/After hooks) — discovery + emitter inline-call insertion.
6. B12 (ReverseMap) — desugar in discovery + ZAMP009 guard.

After all 6: bump `AllocationBudgetTests`, AOT smoke `Program.cs`, commit, push.

---

## Phase A — B10 Skip `[Obsolete]` members (Task 1)

### Task 1: silent skip of `[Obsolete]` source props + destination ctor params

**Files:**
- Modify: `src/ZeroAlloc.Mapping.Generator/PropertyMatcher.cs`
- Create: `tests/ZeroAlloc.Mapping.Generator.Tests/ObsoleteSkipTests.cs`

**Step 1.1: Write failing tests**

Create `tests/ZeroAlloc.Mapping.Generator.Tests/ObsoleteSkipTests.cs`:

```csharp
using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class ObsoleteSkipTests
{
    [Fact]
    public Task Obsolete_SourceProperty_IsSilentlyIgnored()
    {
        var source = """
            using System;
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, [property: Obsolete] int OldField);
            public sealed record Dst(int A);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Obsolete_DestinationParam_TreatedAs_IgnoreTarget()
    {
        var source = """
            using System;
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, [property: Obsolete] string? OldField = null);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
```

**Step 1.2: Run, expect FAIL** (no `.verified.txt` yet)

```
dotnet test tests/ZeroAlloc.Mapping.Generator.Tests -c Release --filter "FullyQualifiedName~ObsoleteSkipTests"
```

Expected: 2 fails (snapshots don't exist).

**Step 1.3: Modify `PropertyMatcher.cs`**

Add a helper at the top of the class:

```csharp
private static bool IsObsolete(ISymbol s) =>
    s.GetAttributes().Any(a =>
        a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute");
```

Modify `Match()` body:
- After `var sourceProps = source.GetMembers().OfType<IPropertySymbol>()…`, add `.Where(p => !IsObsolete(p))` before `.ToDictionary(...)`.
- In the `foreach (var p in ctor.Parameters)` loop, before the `if (constants.TryGetValue(...))` line, add:

```csharp
if (IsObsolete(p)) continue;  // [Obsolete] dest param — silent skip, don't add to unmatched
```

**Step 1.4: Run tests, accept snapshots, re-run**

```
dotnet test tests/ZeroAlloc.Mapping.Generator.Tests -c Release --filter "FullyQualifiedName~ObsoleteSkipTests"
```

For each `.received.txt`:
1. Open the file, verify the generated `Map` body does NOT reference `OldField` and DOES reference all other props.
2. `mv ObsoleteSkipTests.<n>.received.txt ObsoleteSkipTests.<n>.verified.txt`

Re-run; expect 2 PASS.

**Step 1.5: Run full generator tests to verify no regression**

```
dotnet test tests/ZeroAlloc.Mapping.Generator.Tests -c Release
```

Expected: 25/25 PASS (23 prior + 2 new).

**Step 1.6: Commit**

```
git add src/ZeroAlloc.Mapping.Generator/PropertyMatcher.cs tests/ZeroAlloc.Mapping.Generator.Tests/
git commit -m "feat(generator): silent skip of [Obsolete] source/destination members"
```

---

## Phase B — B13 Case-insensitive matching (Task 2)

### Task 2: `[CaseInsensitiveMapping]` opt-in marker

**Files:**
- Create: `src/ZeroAlloc.Mapping/Attributes/CaseInsensitiveMappingAttribute.cs`
- Modify: `src/ZeroAlloc.Mapping/PublicAPI.Unshipped.txt`
- Modify: `src/ZeroAlloc.Mapping.Generator/MapperModel.cs` (extend `MapperClass`)
- Modify: `src/ZeroAlloc.Mapping.Generator/MapperDiscovery.cs` (detect attribute)
- Modify: `src/ZeroAlloc.Mapping.Generator/PropertyMatcher.cs` (use comparer)
- Modify: `src/ZeroAlloc.Mapping.Generator/MapEmitter.cs` (pass the flag through)
- Modify: `src/ZeroAlloc.Mapping.Generator/Diagnostics.cs` (add ZAMP011)
- Modify: `src/ZeroAlloc.Mapping.Generator/MappingGenerator.cs` (fire ZAMP011)
- Create: `tests/ZeroAlloc.Mapping.Generator.Tests/CaseInsensitiveTests.cs`

**Step 2.1: Write failing tests**

```csharp
using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class CaseInsensitiveTests
{
    [Fact]
    public Task CaseInsensitive_Matches_DifferentCasing()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string fooBar);
            public sealed record Dst(string FooBar);
            [Map<Src, Dst>]
            [CaseInsensitiveMapping]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task ZAMP011_AmbiguousCaseInsensitiveMatch_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Foo, string foo);
            public sealed record Dst(string FOO);
            [Map<Src, Dst>]
            [CaseInsensitiveMapping]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP011");
    }
}
```

**Step 2.2: Run, expect FAIL** (`CaseInsensitiveMappingAttribute` doesn't exist; ZAMP011 doesn't exist).

**Step 2.3: Add the runtime attribute**

`src/ZeroAlloc.Mapping/Attributes/CaseInsensitiveMappingAttribute.cs`:

```csharp
namespace ZeroAlloc.Mapping;

/// <summary>
/// Marker — when applied to a <c>[Map]</c>/<c>[TryMap]</c>-decorated <c>static partial class</c>,
/// switches property-name matching to case-insensitive across every declared mapping on that class.
/// Default is case-sensitive.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CaseInsensitiveMappingAttribute : System.Attribute { }
```

Append to `src/ZeroAlloc.Mapping/PublicAPI.Unshipped.txt`:

```
ZeroAlloc.Mapping.CaseInsensitiveMappingAttribute
ZeroAlloc.Mapping.CaseInsensitiveMappingAttribute.CaseInsensitiveMappingAttribute() -> void
```

**Step 2.4: Extend `MapperClass` model**

In `src/ZeroAlloc.Mapping.Generator/MapperModel.cs`, replace the `MapperClass` record with:

```csharp
internal sealed record MapperClass(
    string Namespace,
    string ClassName,
    System.Collections.Generic.IReadOnlyList<MappingDecl> Mappings,
    bool CaseInsensitive = false,
    bool StrictSource = false);
```

**Step 2.5: Detect the attribute in `MapperDiscovery.Discover`**

After the `if (decls.Count == 0) continue;` line, before the `yield return new MapperClass(…)`, compute:

```csharp
var caseInsensitive = type.GetAttributes().Any(a =>
    a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Mapping.CaseInsensitiveMappingAttribute");
```

Pass `CaseInsensitive: caseInsensitive` to the `MapperClass` constructor.

**Step 2.6: Update `PropertyMatcher.Match` signature**

Add a parameter `bool caseInsensitive = false`. Replace:

```csharp
.ToDictionary(p => p.Name, System.StringComparer.Ordinal);
```

with:

```csharp
.ToDictionary(p => p.Name,
    caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal);
```

**Step 2.7: Thread the flag through `MapEmitter`**

In `MapEmitter.Emit`, change the call site:

```csharp
var match = PropertyMatcher.Match(src, dst, decl.UserPartialMethod, cls.CaseInsensitive);
```

Same for `MappingGenerator.ReportPerClassDiagnostics`.

**Step 2.8: Add ZAMP011 to `Diagnostics.cs`**

Append:

```csharp
public static readonly DiagnosticDescriptor ZAMP011_CaseInsensitiveAmbiguous = new(
    id: "ZAMP011",
    title: "Case-insensitive matching produces ambiguous source",
    messageFormat: "Under [CaseInsensitiveMapping], two source properties collide on destination param '{0}' — disambiguate via [MapperIgnoreSource] or [MapProperty]",
    category: "ZeroAlloc.Mapping",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 2.9: Wire ZAMP011 in `MappingGenerator.ReportPerClassDiagnostics`**

After the `match` is computed, when `cls.CaseInsensitive`, scan for ambiguity:

```csharp
if (cls.CaseInsensitive)
{
    var grouped = src.GetMembers().OfType<IPropertySymbol>()
        .Where(p => p.DeclaredAccessibility == Accessibility.Public)
        .GroupBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1);
    foreach (var group in grouped)
    {
        if (match.Constructor.Parameters.Any(p => string.Equals(p.Name, group.Key, System.StringComparison.OrdinalIgnoreCase)))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ZAMP011_CaseInsensitiveAmbiguous,
                decl.Location,
                group.First().Name));
        }
    }
}
```

**Step 2.10: Run tests, accept snapshot, re-run**

```
dotnet test tests/ZeroAlloc.Mapping.Generator.Tests -c Release --filter "FullyQualifiedName~CaseInsensitiveTests"
```

Inspect `CaseInsensitiveTests.CaseInsensitive_Matches_DifferentCasing.received.txt` — verify body uses `FooBar: src.fooBar`. Promote.

**Step 2.11: Run full generator tests + runtime tests**

```
dotnet test ZeroAlloc.Mapping.slnx -c Release
```

Expected: 17/17 runtime + 27/27 generator.

**Step 2.12: Commit**

```
git add src/ZeroAlloc.Mapping/ src/ZeroAlloc.Mapping.Generator/ tests/ZeroAlloc.Mapping.Generator.Tests/
git commit -m "feat(generator): [CaseInsensitiveMapping] opt-in + ZAMP011 ambiguity guard"
```

---

## Phase C — B14 Strict source mode (Task 3)

### Task 3: `[StrictSourceMapping]` + ZAMP010

**Files:**
- Create: `src/ZeroAlloc.Mapping/Attributes/StrictSourceMappingAttribute.cs`
- Modify: `src/ZeroAlloc.Mapping/PublicAPI.Unshipped.txt`
- Modify: `src/ZeroAlloc.Mapping.Generator/MapperDiscovery.cs` (detect attribute)
- Modify: `src/ZeroAlloc.Mapping.Generator/Diagnostics.cs` (add ZAMP010)
- Modify: `src/ZeroAlloc.Mapping.Generator/MappingGenerator.cs` (fire ZAMP010)
- Create: `tests/ZeroAlloc.Mapping.Generator.Tests/StrictSourceTests.cs`

**Step 3.1: Write failing tests**

```csharp
namespace ZeroAlloc.Mapping.Generator.Tests;

public class StrictSourceTests
{
    [Fact]
    public void ZAMP010_UnconsumedSourceProperty_Reported_UnderStrictMode()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B, int C);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            [StrictSourceMapping]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP010");
    }

    [Fact]
    public void Strict_DoesNotFire_WithoutMarker()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B, int C);
            public sealed record Dst(int A);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP010");
    }

    [Fact]
    public void Strict_Honours_MapperIgnoreSource_Suppression()
    {
        // [MapperIgnoreSource] would suppress ZAMP010, but our attribute targets Property declarations.
        // For records this means putting [property: MapperIgnoreSource] on the positional param.
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A, int B, [property: MapperIgnoreSource] int C);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            [StrictSourceMapping]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP010");
    }
}
```

**Step 3.2: Run, expect FAIL**

**Step 3.3: Add the attribute + PublicAPI entries**

`src/ZeroAlloc.Mapping/Attributes/StrictSourceMappingAttribute.cs`:

```csharp
namespace ZeroAlloc.Mapping;

/// <summary>
/// Marker — when applied to a <c>[Map]</c>/<c>[TryMap]</c>-decorated <c>static partial class</c>,
/// requires every source property to be either consumed by a destination parameter or marked
/// <c>[MapperIgnoreSource]</c>. Unconsumed sources fire ZAMP010 (Error). Default is permissive.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StrictSourceMappingAttribute : System.Attribute { }
```

Append to PublicAPI.Unshipped.txt.

**Step 3.4: Detect in `MapperDiscovery`**

Same pattern as CaseInsensitive (see Task 2.5). Compute `strictSource` and pass `StrictSource: strictSource` to `MapperClass`.

**Step 3.5: Add ZAMP010 to `Diagnostics.cs`**

```csharp
public static readonly DiagnosticDescriptor ZAMP010_UnconsumedSource = new(
    id: "ZAMP010",
    title: "Source property is not consumed under strict source mapping",
    messageFormat: "Under [StrictSourceMapping], source property '{0}' on '{1}' is not consumed by any destination parameter and not marked [MapperIgnoreSource]",
    category: "ZeroAlloc.Mapping",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 3.6: Fire ZAMP010 in `MappingGenerator.ReportPerClassDiagnostics`**

After per-pair processing, for each declaration if `cls.StrictSource`:

```csharp
if (cls.StrictSource && decl.Kind == MappingKind.Map || cls.StrictSource && decl.Kind == MappingKind.TryMap)
{
    var consumed = new System.Collections.Generic.HashSet<string>(
        match.Mappings.Select(m => m.SourcePropertyName), System.StringComparer.Ordinal);
    foreach (var p in src.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility == Accessibility.Public))
    {
        if (consumed.Contains(p.Name)) continue;
        var ignore = p.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Mapping.MapperIgnoreSourceAttribute");
        if (ignore) continue;
        spc.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.ZAMP010_UnconsumedSource,
            decl.Location, p.Name, src.ToDisplayString()));
    }
}
```

**Step 3.7: Run, expect 3 PASS**

**Step 3.8: Run full slnx tests**

Expected: 17/17 runtime + 30/30 generator (27 + 3 new).

**Step 3.9: Commit**

```
git add src/ZeroAlloc.Mapping/ src/ZeroAlloc.Mapping.Generator/ tests/ZeroAlloc.Mapping.Generator.Tests/
git commit -m "feat(generator): [StrictSourceMapping] opt-in + ZAMP010 unconsumed-source diagnostic"
```

---

## Phase D — B1 Flattening (Task 4)

### Task 4: dotted-path support in `[MapProperty]`

**Files:**
- Modify: `src/ZeroAlloc.Mapping.Generator/PropertyMatcher.cs` (parse dotted source paths)
- Modify: `src/ZeroAlloc.Mapping.Generator/MapEmitter.cs` (emit `src.A?.B?.C` or `src.A!.B!.C`)
- Modify: `src/ZeroAlloc.Mapping.Generator/MappingGenerator.cs` (extend ZAMP005 to validate path segments)
- Create: `tests/ZeroAlloc.Mapping.Generator.Tests/FlatteningTests.cs`

**Step 4.1: Write failing tests**

```csharp
using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class FlatteningTests
{
    [Fact]
    public Task Flatten_TwoLevels_Emits_NullForgivingPath_Under_Map()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Address(string City);
            public sealed record Customer(Address Address);
            public sealed record Src(Customer Customer);
            public sealed record Dst(string City);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Customer.Address.City", "City")]
                public static partial Dst Map(Src src);
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Flatten_NullableSource_EmitsNullConditional()
    {
        var source = """
            #nullable enable
            using ZeroAlloc.Mapping;
            public sealed record Address(string City);
            public sealed record Customer(Address? Address);
            public sealed record Src(Customer? Customer);
            public sealed record Dst(string? City);
            [Map<Src?, Dst?>]
            public static partial class M
            {
                [MapProperty("Customer.Address.City", "City")]
                public static partial Dst? Map(Src? src);
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void ZAMP005_MissingDottedSegment_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Address(string City);
            public sealed record Customer(Address Address);
            public sealed record Src(Customer Customer);
            public sealed record Dst(string City);
            [Map<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Customer.NoSuchProp.City", "City")]
                public static partial Dst Map(Src src);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP005");
    }
}
```

**Step 4.2: Run, expect FAIL**

**Step 4.3: Extend `PropertyMatcher` — recognize dotted source**

Replace the `PropertyMapping` record (`src/ZeroAlloc.Mapping.Generator/PropertyMatcher.cs`) with one carrying the full dotted name:

```csharp
internal sealed record PropertyMapping(
    string TargetParamName,
    string SourcePropertyName,           // dot-separated for flattening, single name otherwise
    ITypeSymbol SourceType,              // type of the leaf property
    ITypeSymbol TargetType,
    bool IsFlattened = false);
```

In `Match`, extend the rename-resolution branch:

```csharp
var sourceName = renames.TryGetValue(p.Name, out var rename) ? rename : p.Name;

if (sourceName.Contains('.'))
{
    var (leaf, leafType) = WalkDottedPath(source, sourceName);
    if (leaf is null) { unmatched.Add(p.Name); continue; }
    mappings.Add(new PropertyMapping(p.Name, sourceName, leafType!, p.Type, IsFlattened: true));
    continue;
}
```

Add a static helper:

```csharp
private static (IPropertySymbol? Leaf, ITypeSymbol? LeafType) WalkDottedPath(INamedTypeSymbol root, string dottedPath)
{
    INamedTypeSymbol? cursor = root;
    IPropertySymbol? leaf = null;
    foreach (var segment in dottedPath.Split('.'))
    {
        if (cursor is null) return (null, null);
        leaf = cursor.GetMembers(segment).OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.DeclaredAccessibility == Accessibility.Public);
        if (leaf is null) return (null, null);
        cursor = leaf.Type as INamedTypeSymbol;
    }
    return (leaf, leaf?.Type);
}
```

**Step 4.4: Extend `MapEmitter.ResolveExpression`**

Inside `ResolveExpression`, before the standard conversion path, add:

```csharp
if (m.IsFlattened)
{
    // Decide null-conditional vs null-forgiving.
    // Rule: if either source root or any segment is annotated nullable → use ?., otherwise use !.
    var op = NeedsNullConditional(m.SourceType) ? "?." : ".";
    return "src." + m.SourcePropertyName.Replace(".", op);
}
```

with helper:

```csharp
private static bool NeedsNullConditional(ITypeSymbol leafType) =>
    leafType.NullableAnnotation == NullableAnnotation.Annotated;
```

(Heuristic — refine if needed: for v1 conservatively use `?.` whenever the destination param accepts null, else `!.`. Tests will pin down the exact behaviour.)

**Step 4.5: Extend ZAMP005 in `MappingGenerator`**

In the existing ZAMP005 block, when validating `[MapProperty]` source name, if it contains `.`, walk the path and report ZAMP005 for the first missing segment.

```csharp
if (srcName is not null)
{
    if (srcName.Contains('.'))
    {
        // Validate every segment.
        INamedTypeSymbol? cursor = src;
        var segments = srcName.Split('.');
        foreach (var segment in segments)
        {
            if (cursor is null) break;
            var found = cursor.GetMembers(segment).OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.DeclaredAccessibility == Accessibility.Public);
            if (found is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ZAMP005_MapPropertyTargetMissing,
                    decl.Location, segment, cursor.ToDisplayString()));
                break;
            }
            cursor = found.Type as INamedTypeSymbol;
        }
    }
    else if (!sourceProps.Contains(srcName))
    {
        spc.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.ZAMP005_MapPropertyTargetMissing,
            decl.Location, srcName, src.ToDisplayString()));
    }
}
```

(Adjust the existing `if (srcName is not null && !sourceProps.Contains(srcName))` block accordingly.)

**Step 4.6: Run tests, accept snapshots**

```
dotnet test tests/ZeroAlloc.Mapping.Generator.Tests -c Release --filter "FullyQualifiedName~FlatteningTests"
```

For `Flatten_TwoLevels_…` expect emission like `City: src.Customer!.Address!.City`. For `Flatten_NullableSource_…` expect `?.`.

Promote both snapshots.

**Step 4.7: Run full slnx tests + verify no regression**

```
dotnet test ZeroAlloc.Mapping.slnx -c Release
```

Expected: 17/17 runtime + 33/33 generator.

**Step 4.8: Commit**

```
git add src/ZeroAlloc.Mapping.Generator/ tests/ZeroAlloc.Mapping.Generator.Tests/
git commit -m "feat(generator): [MapProperty] flattening via dotted source path + ZAMP005 segment-walk"
```

---

## Phase E — B4 `[BeforeMap]` / `[AfterMap]` hooks (Task 5)

### Task 5: hook attributes + inline emission

**Files:**
- Create: `src/ZeroAlloc.Mapping/Attributes/BeforeMapAttribute.cs`
- Create: `src/ZeroAlloc.Mapping/Attributes/AfterMapAttribute.cs`
- Modify: `src/ZeroAlloc.Mapping/PublicAPI.Unshipped.txt`
- Modify: `src/ZeroAlloc.Mapping.Generator/MapperModel.cs` (add `Hooks` to `MapperClass`)
- Modify: `src/ZeroAlloc.Mapping.Generator/MapperDiscovery.cs` (collect hooks)
- Modify: `src/ZeroAlloc.Mapping.Generator/MapEmitter.cs` (inline before/after calls)
- Modify: `src/ZeroAlloc.Mapping.Generator/TryMapEmitter.cs` (same, inside try-block)
- Create: `tests/ZeroAlloc.Mapping.Generator.Tests/HooksTests.cs`

**Step 5.1: Write failing tests**

```csharp
using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class HooksTests
{
    [Fact]
    public Task BeforeMap_Hook_Inlined_BeforeConstructor()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>]
            public static partial class M
            {
                [BeforeMap]
                public static void Validate(Src src) { }
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task AfterMap_Hook_Inlined_AfterAssignment()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [Map<Src, Dst>]
            public static partial class M
            {
                [AfterMap]
                public static void Audit(Src src, Dst dst) { }
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task TryMap_Hooks_Live_Inside_TryBlock()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [TryMap<Src, Dst>]
            public static partial class M
            {
                [BeforeMap]
                public static void Validate(Src src) { }
                [AfterMap]
                public static void Audit(Src src, Dst dst) { }
            }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
```

**Step 5.2: Run, expect FAIL**

**Step 5.3: Add the runtime attributes + PublicAPI**

`BeforeMapAttribute.cs`:

```csharp
namespace ZeroAlloc.Mapping;

/// <summary>
/// Marks a static method on a <c>[Map]</c>/<c>[TryMap]</c>-decorated class as a hook invoked
/// before each generated mapping body. Method signature:
/// <c>static void Hook(TSource src)</c>. Multiple <c>[BeforeMap]</c> hooks may be declared;
/// they fire in declaration order.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class BeforeMapAttribute : System.Attribute { }
```

`AfterMapAttribute.cs`:

```csharp
namespace ZeroAlloc.Mapping;

/// <summary>
/// Marks a static method on a <c>[Map]</c>/<c>[TryMap]</c>-decorated class as a hook invoked
/// after each generated mapping body, with both source and destination available. Signature:
/// <c>static void Hook(TSource src, TDestination dst)</c>. Multiple hooks fire in declaration order.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class AfterMapAttribute : System.Attribute { }
```

Add corresponding lines to `PublicAPI.Unshipped.txt`.

**Step 5.4: Extend `MapperClass` model**

```csharp
internal sealed record HookMethod(string MethodName, ITypeSymbol[] ParamTypes, bool IsAfter);

internal sealed record MapperClass(
    string Namespace,
    string ClassName,
    System.Collections.Generic.IReadOnlyList<MappingDecl> Mappings,
    bool CaseInsensitive = false,
    bool StrictSource = false,
    System.Collections.Generic.IReadOnlyList<HookMethod>? Hooks = null);
```

**Step 5.5: Collect hooks in `MapperDiscovery.Discover`**

After detecting class-level markers but before yielding:

```csharp
var hooks = new System.Collections.Generic.List<HookMethod>();
foreach (var m in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsStatic))
{
    foreach (var attr in m.GetAttributes())
    {
        var name = attr.AttributeClass?.Name;
        var isBefore = name == "BeforeMapAttribute";
        var isAfter = name == "AfterMapAttribute";
        if (!isBefore && !isAfter) continue;
        hooks.Add(new HookMethod(
            MethodName: m.Name,
            ParamTypes: m.Parameters.Select(p => p.Type).ToArray(),
            IsAfter: isAfter));
    }
}
```

Pass `Hooks: hooks` to the `MapperClass` constructor.

**Step 5.6: Wire hook emission in `MapEmitter.EmitMapMethod`**

Before emitting `return new ...`, accumulate matching `Before` hooks (those whose first param type is assignable from `decl.SourceType`), insert `Foo(src);` calls. After emitting the `new` block, switch to assigning `var __dst = …` and emit `After` hook calls (`Bar(src, __dst);`), then `return __dst;`.

Detail — replace the existing single-line `return new …;` with:

```csharp
foreach (var hook in MatchingHooks(owningClass, decl, isAfter: false))
    sb.Append("        ").Append(hook.MethodName).Append("(src);\n");

sb.Append("        var __dst = new ").Append(decl.DestinationTypeFqn).Append("(\n");
// … existing per-property assignment loop unchanged …
sb.Append("        );\n");

foreach (var hook in MatchingHooks(owningClass, decl, isAfter: true))
    sb.Append("        ").Append(hook.MethodName).Append("(src, __dst);\n");

sb.Append("        return __dst;\n    }\n");
```

with helper (in `MapEmitter`):

```csharp
private static System.Collections.Generic.IEnumerable<HookMethod> MatchingHooks(MapperClass cls, MappingDecl decl, bool isAfter)
{
    if (cls.Hooks is null) yield break;
    foreach (var h in cls.Hooks.Where(h => h.IsAfter == isAfter))
    {
        if (isAfter)
        {
            if (h.ParamTypes.Length != 2) continue;
        }
        else
        {
            if (h.ParamTypes.Length != 1) continue;
        }
        // Coarse compatibility check — generator emits the call; the C# compiler does the real validation.
        yield return h;
    }
}
```

**Step 5.7: Same hook insertion in `TryMapEmitter`** — but the `var __dst = new …` lives inside `try { … }`, hooks too.

**Step 5.8: Run tests, accept snapshots, re-run**

Inspect each `.received.txt`:
- `BeforeMap_Hook_…`: expect `Validate(src);` line above the `new Dst(…)`.
- `AfterMap_Hook_…`: expect `var __dst = new Dst(…); Audit(src, __dst); return __dst;`.
- `TryMap_Hooks_…`: expect both calls inside `try { }`.

Promote.

**Step 5.9: Run full slnx tests**

Expected: 17/17 runtime + 36/36 generator.

**Step 5.10: Commit**

```
git add src/ZeroAlloc.Mapping/ src/ZeroAlloc.Mapping.Generator/ tests/ZeroAlloc.Mapping.Generator.Tests/
git commit -m "feat(generator): [BeforeMap] / [AfterMap] hooks emitted inline"
```

---

## Phase F — B12 `[ReverseMap<,>]` (Task 6)

### Task 6: ReverseMap desugar + ZAMP009 guard

**Files:**
- Create: `src/ZeroAlloc.Mapping/Attributes/ReverseMapAttribute.cs`
- Create: `src/ZeroAlloc.Mapping/Attributes/ReverseTryMapAttribute.cs`
- Modify: `src/ZeroAlloc.Mapping/PublicAPI.Unshipped.txt`
- Modify: `src/ZeroAlloc.Mapping.Generator/MapperDiscovery.cs` (desugar Reverse* into 2 decls)
- Modify: `src/ZeroAlloc.Mapping.Generator/Diagnostics.cs` (add ZAMP009)
- Modify: `src/ZeroAlloc.Mapping.Generator/MappingGenerator.cs` (fire ZAMP009)
- Create: `tests/ZeroAlloc.Mapping.Generator.Tests/ReverseMapTests.cs`

**Step 6.1: Write failing tests**

```csharp
using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class ReverseMapTests
{
    [Fact]
    public Task ReverseMap_Emits_Both_Directions()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Order(int Id, string Notes);
            public sealed record OrderDto(int Id, string Notes);
            [ReverseMap<Order, OrderDto>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public void ZAMP009_ReverseMap_With_MapProperty_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Foo);
            public sealed record Dst(string Bar);
            [ReverseMap<Src, Dst>]
            public static partial class M
            {
                [MapProperty("Foo", "Bar")]
                public static partial Dst Map(Src src);
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP009");
    }
}
```

**Step 6.2: Run, expect FAIL**

**Step 6.3: Add attributes**

`ReverseMapAttribute.cs`:

```csharp
namespace ZeroAlloc.Mapping;

/// <summary>
/// Convenience: declares a symmetric mapping. Generator emits both
/// <c>static TDestination Map(TSource)</c> and <c>static TSource Map(TDestination)</c>.
/// Equivalent to <c>[Map&lt;TSrc, TDst&gt;]</c> + <c>[Map&lt;TDst, TSrc&gt;]</c>.
/// Customisations on the partial method (<c>[MapProperty]</c>, <c>[MapValue]</c>,
/// <c>[MapperIgnoreTarget]</c>) are not safely reversible — generator emits ZAMP009.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ReverseMapAttribute<TSource, TDestination> : System.Attribute { }
```

`ReverseTryMapAttribute.cs`: same shape, both directions emit `[TryMap]`-style methods.

Append PublicAPI entries.

**Step 6.4: Desugar in `MapperDiscovery.Discover`**

In the attribute-iteration loop, after handling `[Map<,>]` and `[TryMap<,>]`, add:

```csharp
var reverseMapAttr = comp.GetTypeByMetadataName("ZeroAlloc.Mapping.ReverseMapAttribute`2");
var reverseTryMapAttr = comp.GetTypeByMetadataName("ZeroAlloc.Mapping.ReverseTryMapAttribute`2");
// …
if (reverseMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, reverseMapAttr))
{
    // Emit two MappingDecl entries.
    var fwdPartial = FindUserPartialMethod(type, MappingKind.Map, typeArgs[0], typeArgs[1]);
    var revPartial = FindUserPartialMethod(type, MappingKind.Map, typeArgs[1], typeArgs[0]);
    decls.Add(new MappingDecl(typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                              typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                              MappingKind.Map, type.Locations.FirstOrDefault() ?? Location.None, fwdPartial));
    decls.Add(new MappingDecl(typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                              typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                              MappingKind.Map, type.Locations.FirstOrDefault() ?? Location.None, revPartial));
    continue;
}
// Same for ReverseTryMap with kind = MappingKind.TryMap.
```

Pull the `mapAttr` / `tryMapAttr` lookup into the function body alongside the new lookups.

**Step 6.5: Add ZAMP009 to `Diagnostics.cs`**

```csharp
public static readonly DiagnosticDescriptor ZAMP009_ReverseMapNotSymmetric = new(
    id: "ZAMP009",
    title: "[ReverseMap] is not safely reversible",
    messageFormat: "[ReverseMap<{0}, {1}>] cannot be auto-reversed because the partial method declares '{2}', which is information-asymmetric — write two explicit [Map<,>]s instead",
    category: "ZeroAlloc.Mapping",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 6.6: Fire ZAMP009 in `MappingGenerator.ReportPerClassDiagnostics`**

Track which `MappingDecl` came from a `ReverseMap` desugar (add a `bool FromReverse = false` flag to `MappingDecl`; set true when desugaring). For decls where `FromReverse` and the `UserPartialMethod` carries `[MapProperty]`/`[MapValue]`/`[MapperIgnoreTarget]`, fire ZAMP009.

```csharp
if (decl.FromReverse && decl.UserPartialMethod is not null)
{
    foreach (var attr in decl.UserPartialMethod.GetAttributes())
    {
        var name = attr.AttributeClass?.Name;
        if (name == "MapPropertyAttribute" || name == "MapValueAttribute" || name == "MapperIgnoreTargetAttribute")
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ZAMP009_ReverseMapNotSymmetric,
                decl.Location,
                src.ToDisplayString(),
                dst.ToDisplayString(),
                "[" + name!.Replace("Attribute", "") + "]"));
            break;
        }
    }
}
```

**Step 6.7: Run, accept snapshots**

Inspect `ReverseMap_Emits_Both_Directions.received.txt` — expect TWO method bodies (`Map(Order) → OrderDto` and `Map(OrderDto) → Order`).

Promote.

**Step 6.8: Run full slnx tests**

Expected: 17/17 runtime + 38/38 generator.

**Step 6.9: Commit**

```
git add src/ZeroAlloc.Mapping/ src/ZeroAlloc.Mapping.Generator/ tests/ZeroAlloc.Mapping.Generator.Tests/
git commit -m "feat(generator): [ReverseMap<,>] desugars to bidirectional [Map<,>] + ZAMP009 guard"
```

---

## Phase G — Allocation budgets + AOT smoke (Task 7)

### Task 7: extend the AOT smoke + allocation tests with new features

**Files:**
- Modify: `samples/ZeroAlloc.Mapping.AotSmoke/Program.cs` (exercise hooks + flatten + reverse)
- Modify: `tests/ZeroAlloc.Mapping.Tests/AllocationBudgetTests.cs` (add 1 budget per non-trivial new feature)

**Step 7.1: Extend AOT smoke**

In `samples/ZeroAlloc.Mapping.AotSmoke/Program.cs`, add — adjacent to existing `OrderRequest`/`Order` types — small fixture types and declarations to exercise:

- One `[BeforeMap]` + `[AfterMap]` hook on the existing `Mappings` class (no-op bodies — the goal is `aot-smoke` confirms AOT-publish-ability).
- One `[ReverseMap<,>]` declaration on a new `static partial class ReverseFixtures` with two trivial record types.
- One `[MapProperty("A.B", "AB")]` flatten on a `static partial class FlattenFixtures` with a 2-deep source.

In `Program.Main`, add `_ = ReverseFixtures.Map(…); _ = FlattenFixtures.Map(…);` calls. The point is to run the generated code in JIT mode end-to-end; CI's `aot-smoke` job confirms AOT publishability.

**Step 7.2: Add allocation budgets**

In `tests/ZeroAlloc.Mapping.Tests/AllocationBudgetTests.cs`, append three new tests:

```csharp
[Fact]
public void Map_With_BeforeAfterHooks_WithinBudget()
{
    var req = new OrderRequest(1, "n");
    AllocationGate.AssertBudget(80, 1000, () => _ = BudgetMappings.Map(req), "[Map] with hooks");
}
// — and two more, one per non-trivial new feature added to the BudgetMappings host class —
```

(Add corresponding hook declarations + a `[ReverseMap<,>]` and `[MapProperty(dotted, …)]` to the `BudgetMappings` class to actually exercise these in a budget.)

**Step 7.3: Run all tests**

```
dotnet test ZeroAlloc.Mapping.slnx -c Release
```

Expected: 20/20 runtime (17 + 3) + 38/38 generator. AOT smoke binary still exits 0 in JIT mode.

**Step 7.4: Run AOT binary**

```
dotnet run --project samples/ZeroAlloc.Mapping.AotSmoke -c Release
```

Expected output: `AOT mapping behavior OK` then `AOT allocation gate OK`.

**Step 7.5: Commit**

```
git add samples/ tests/ZeroAlloc.Mapping.Tests/
git commit -m "test(certify): allocation budgets + AOT smoke for v1 extensions"
```

---

## Phase H — backlog bookkeeping (Task 8)

### Task 8: prune `docs/backlog.md`

**Files:**
- Modify: `docs/backlog.md`

**Step 8.1: Remove B1, B4, B10, B12, B13, B14**

Delete the corresponding sections entirely. Renumber remaining items B2/B3/B5/B6/B7/B8/B9/B11 → keep as-is (the IDs don't need renumbering; they're identifiers, not ordinals).

Add a note at the top of the file:

```markdown
> **Update 2026-05-07:** B1 (flattening), B4 (hooks), B10 (`[Obsolete]` skip),
> B12 (`[ReverseMap]`), B13 (case-insensitive), B14 (strict source) graduated
> into v1 — see [`plans/2026-05-07-mapping-v1-extensions-design.md`](plans/2026-05-07-mapping-v1-extensions-design.md).
```

**Step 8.2: Commit**

```
git add docs/backlog.md
git commit -m "docs(backlog): prune items graduated into v1 (B1, B4, B10, B12, B13, B14)"
```

---

## Phase I — push + update PR (Task 9)

### Task 9: push the new commits to the existing PR

**Step 9.1: Verify everything builds + tests pass**

```
dotnet build ZeroAlloc.Mapping.slnx -c Release
dotnet test ZeroAlloc.Mapping.slnx -c Release --no-build
```

Expected: 0 errors, all tests pass.

**Step 9.2: Push**

```
git push origin feat/v1-scaffold-and-runtime
```

(no PR creation — PR #1 is already open and tracks this branch.)

**Step 9.3: Watch CI**

```
gh pr checks 1 --repo ZeroAlloc-Net/ZeroAlloc.Mapping --watch
```

Expected: `lint-commits`, `build`, `aot-smoke`, `api-compat` all green.

**Step 9.4: Add a PR comment summarising the additions**

```bash
gh pr comment 1 --repo ZeroAlloc-Net/ZeroAlloc.Mapping --body "$(cat <<'EOF'
## v1 extensions added (PR #1 grew)

Six items promoted from `docs/backlog.md` and merged into the v1 scope:

| ID | Feature |
|---|---|
| B1 | Flattening — `[MapProperty("A.B.C", "ABC")]` dotted source paths |
| B4 | `[BeforeMap]` / `[AfterMap]` hooks |
| B10 | Silent skip of `[Obsolete]` source/destination members |
| B12 | `[ReverseMap<,>]` — bidirectional desugar with ZAMP009 safety guard |
| B13 | `[CaseInsensitiveMapping]` — class-level opt-in + ZAMP011 ambiguity guard |
| B14 | `[StrictSourceMapping]` — class-level opt-in + ZAMP010 unconsumed-source diagnostic |

Each is opt-in (no v1-surface breakage), each ships behind snapshot tests, allocation budgets cover the non-trivial ones. ZAMP009/010/011 added.

Design: [`docs/plans/2026-05-07-mapping-v1-extensions-design.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/feat/v1-scaffold-and-runtime/docs/plans/2026-05-07-mapping-v1-extensions-design.md)
Plan: [`docs/plans/2026-05-07-mapping-v1-extensions.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/blob/feat/v1-scaffold-and-runtime/docs/plans/2026-05-07-mapping-v1-extensions.md)

Backlog now down to B2/B3/B5/B6/B7/B8/B9/B11.
EOF
)"
```

---

## Done

After Tasks 1-9:

- 6 backlog items graduated into v1 (B1, B4, B10, B12, B13, B14).
- 5 new attributes shipped (`CaseInsensitiveMapping`, `StrictSourceMapping`, `BeforeMap`, `AfterMap`, `ReverseMap<,>` + `ReverseTryMap<,>`).
- 3 new diagnostics (ZAMP009/010/011); ZAMP005 extended for dotted paths.
- ~15 new generator tests, ~3 new runtime tests, AOT smoke + 3 new allocation budgets.
- PR #1 grew but stayed reviewable; `aot-smoke` + `api-compat` + `build` all stayed green.
- `docs/backlog.md` pruned to 8 surviving items.
