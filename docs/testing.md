---
id: testing
title: Testing
description: Snapshot patterns for generator output, allocation gates for runtime budgets, and diagnostic verification.
sidebar_position: 13
---

# Testing

There are three layers worth covering: the application code that calls generated mappers, the generated source itself (for library extenders), and the diagnostics the generator emits.

## Testing user code

The generated `Map` / `TryMap` methods are static, pure, and deterministic. There's nothing to mock — assert on the returned value:

```csharp
public sealed record OrderRequest(int Id, string Notes);
public sealed record Order(int Id, string Notes);

[Map<OrderRequest, Order>]
public static partial class AppMappings { }

public class AppMappingsTests
{
    [Fact]
    public void Map_TwoIdenticalProperties()
    {
        var order = AppMappings.Map(new OrderRequest(42, "rush"));
        Assert.Equal(42, order.Id);
        Assert.Equal("rush", order.Notes);
    }

    [Fact]
    public void TryMap_NullSource_Returns_RootError()
    {
        var result = AppMappings.TryMap(null);
        Assert.True(result.IsFailure);
        Assert.Equal("mapping.source.null", result.Error.Code);
        Assert.Equal("(root)", result.Error.PropertyPath);
    }
}
```

That's the bulk of consumer testing — call the generated method, assert on the result.

## Snapshot pattern with Verify

For library extenders or anyone hardening contributions to the generator, snapshot the emitted source via `TestHarness.RunGenerator(string source)` (see `tests/ZeroAlloc.Mapping.Generator.Tests/TestHarness.cs`):

```csharp
using VerifyXunit;

public class FlatMapEmissionTests
{
    [Fact]
    public Task FlatMap_Emits_DirectAssignment()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(int X);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source))
                       .UseDirectory("Snapshots");
    }
}
```

`RunGenerator` builds a one-file compilation, runs the generator, and concatenates every emitted source with a `// ===== next file =====` separator. Verify diff-rejects on first run; review the diff and accept it once the change is intentional.

The existing `Snapshots/` directory under `tests/ZeroAlloc.Mapping.Generator.Tests/` contains the canonical output for every supported feature — flatten, polymorphic, hooks, update-in-place, culture, reverse-map. Use those as oracles when adding a new emission path.

## Allocation gates

`AllocationGate.AssertBudget` (`samples/ZeroAlloc.Mapping.AotSmoke/Internal/AllocationGate.cs`) is the runtime allocation budget gate. Use it to lock down a per-call byte budget for any code path you care about:

```csharp
[Fact]
public void Map_FlatRecord_WithinBudget()
{
    var req = new OrderRequest(1, "n");
    AllocationGate.AssertBudget(80, 1000,
        () => _ = AppMappings.Map(req),
        "[Map] flat");
}
```

Mechanics: the gate warms with two calls, forces a full GC, snapshots `GC.GetAllocatedBytesForCurrentThread()`, runs the action `iterations` times, and asserts the delta is within `budgetBytes * iterations`. The thread-local counter avoids cross-test interference.

The full set of CI-enforced budgets lives in `tests/ZeroAlloc.Mapping.Tests/AllocationBudgetTests.cs`. See [Performance](performance.md) for the budget table and methodology.

## Diagnostic tests

Verify a `ZAMP*` diagnostic fires on the source pattern that should trigger it, using `TestHarness.RunDiagnostics(string)`:

```csharp
public class DiagnosticTests
{
    [Fact]
    public void ZAMP001_DestinationHasNoSource_Reported()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int A);
            public sealed record Dst(int A, int B);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP001");
    }

    [Fact]
    public void Clean_Source_Emits_No_Diagnostics()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record OrderRequest(int Id, string Notes);
            public sealed record Order(int Id, string Notes);
            [Map<OrderRequest, Order>]
            public static partial class AppMappings { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Empty(diags);
    }
}
```

Two patterns to keep paired: a positive test that asserts the diagnostic fires for the bad source, and a negative test that asserts a clean source produces no diagnostics. Both protect against regressions in opposite directions — one against a missing detector, one against a false positive.

For a feature that gates on a marker attribute (`[StrictSourceMapping]`, `[CaseInsensitiveMapping]`), add a third test asserting the diagnostic does **not** fire without the marker. Existing examples: `StrictSourceTests.Strict_DoesNotFire_WithoutMarker`, `CaseInsensitiveTests.ZAMP011_DoesNotFire_When_ObsoleteSource_Collides`, `DuplicateMappingCultureTests.ZAMP016_Single_MappingCulture_DoesNotFire`.

See [Diagnostics](diagnostics.md) for the full ZAMP001-016 list with triggering source and fix examples.

## Where to next

- Diagnostics: [Diagnostics](diagnostics.md).
- Performance: [Performance](performance.md).
