# AGENTS.md: ZeroAlloc.Mapping

ZeroAlloc.Mapping is a source-generated object mapper for .NET. Consumers annotate a static partial class with
`[Map<,>]`, `[TryMap<,>]` or `[ReverseMap<,>]` and the Roslyn generator writes plain constructor calls and
property assignments at compile time: no reflection, NativeAOT-safe. One NuGet package ships,
`ZeroAlloc.Mapping`, with the generator bundled inside it.

## Layout

- `src/ZeroAlloc.Mapping` is the runtime package: attributes and `MappingError`. It multi-targets
  `net10.0;net11.0`.
- `src/ZeroAlloc.Mapping.Generator` is the incremental generator, `netstandard2.0`, `IsPackable=false`. It is
  not published on its own: the runtime csproj packs its DLL into `analyzers/dotnet/cs`. Generator code must
  stay within the netstandard2.0 API and the extended analyzer rules (`EnforceExtendedAnalyzerRules`).
- Generator test snapshots live in `tests/ZeroAlloc.Mapping.Generator.Tests/Snapshots` as `*.verified.*`
  files. They are data, excluded from compilation, and a generator change that alters emitted code needs
  them regenerated and reviewed.
- `samples/ZeroAlloc.Mapping.AotSmoke` is a NativeAOT executable, `net11.0` only. `benchmarks/` is not
  shipped.
- `docs/` is published to the ZeroAlloc website (a push touching `docs/**` triggers a rebuild). `tools/import-benchmarks.ps1`
  splices benchmark ratios and allocations into `docs/performance.md` between its `BENCH:START` and `BENCH:END` markers.

## Build and test

Prerequisites: `global.json` pins SDK 11.0.100 (`rollForward: latestMinor`), so an SDK 11 is required.
The runtime package and the test projects target `net10.0;net11.0`, so `dotnet test`
needs the .NET 10 and .NET 11 runtimes. The AOT job also needs `clang` and `zlib1g-dev`.

CI job `build` in `.github/workflows/ci.yml`:

```bash
dotnet tool restore
VERSION=$(dotnet gitversion /showvariable SemVer)
dotnet restore
dotnet build --no-restore -c Release -p:Version=$VERSION
dotnet test --no-build -c Release --verbosity normal --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
dotnet pack src/ZeroAlloc.Mapping/ZeroAlloc.Mapping.csproj --no-build -c Release -p:PackageVersion=$VERSION -o ./artifacts
```

CI job `aot-smoke` in `.github/workflows/ci.yml`:

```bash
dotnet restore samples/ZeroAlloc.Mapping.AotSmoke/ZeroAlloc.Mapping.AotSmoke.csproj
dotnet publish samples/ZeroAlloc.Mapping.AotSmoke/ZeroAlloc.Mapping.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out
timeout --kill-after=10s 120s ./aot-out/ZeroAlloc.Mapping.AotSmoke
```

Other CI jobs: `api-compat` and `release-tracking` (shared workflows from ZeroAlloc-Net/.github),
`baseline-checks.yml` (gitleaks, CodeQL, NuGetAudit), and `benchmarks.yml` (a smoke run of
`benchmarks/ZeroAlloc.Mapping.Benchmarks` through the shared benchmark-smoke workflow).

Inner loop, verified locally, not a CI job:

```bash
dotnet test tests/ZeroAlloc.Mapping.Generator.Tests -c Release -f net10.0 --filter "FullyQualifiedName~HintNameTests"
dotnet test tests/ZeroAlloc.Mapping.Tests -c Release -f net10.0 --filter "FullyQualifiedName~AllocationBudget"
ZA_SNAPSHOT_UPDATE=1 dotnet test tests/ZeroAlloc.Mapping.Generator.Tests -c Release -f net10.0
```

The last command rewrites the generator snapshots; read the diff of `*.verified.*` before committing.
`tests/ZeroAlloc.Mapping.Generator.Tests` runs the generator in-process through Roslyn (`TestHarness`);
`tests/ZeroAlloc.Mapping.Tests` compiles real mappers and asserts behaviour and allocation budgets.

CI job `sdk10-consumer` needs a 10.0.1xx SDK; `tests/ZeroAlloc.Mapping.Sdk10Smoke/global.json` pins
10.0.100 with `rollForward: latestPatch`, so a newer band cannot stand in for it. It builds and runs a
consumer of the packed packages with Roslyn 5.0:

```bash
v=9.9.9-local.sha$(git rev-parse --short HEAD).$(date +%s)
dotnet pack src/ZeroAlloc.Mapping -c Release -o artifacts -p:Version="$v"
cd tests/ZeroAlloc.Mapping.Sdk10Smoke/Consumer
dotnet run -c Release -p:ZeroAllocMappingVersion="$v"
```

CI job `roslyn-floor` calls the shared `roslyn-floor.yml` from ZeroAlloc-Net/.github: it packs the
solution and fails when a packed DLL references Roslyn above 5.0.0.

## Conventions and gotchas
- The generator references `Microsoft.CodeAnalysis.CSharp` 5.0.0, the compiler of SDK band 10.0.1xx, the
  oldest supported SDK. A compiler older than the Roslyn the generator references skips it with `CS9057`,
  and nothing is generated. The shared Renovate preset holds it below 5.1. If a newer Roslyn API is
  needed, change the code; raise the floor only together with the minimum SDK. Test projects may use a
  newer Roslyn.

- Warnings are errors (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`), with Meziantou, Roslynator and
  ZeroAlloc.Analyzers on every non-netstandard2.0 project. Fix the diagnostic; do not add `NoWarn`. The only
  `MA0016` suppression is scoped to `tests/` and `samples/`, where concrete `List<T>` is the scenario.
- The runtime package turns on `IsAotCompatible` and makes the trim, single-file and AOT warnings errors
  (IL2026, IL3050 and others). Do not add reflection or `dynamic` to it or to generated output.
- Public API is tracked: a change to the runtime package's public surface needs a line in
  `src/ZeroAlloc.Mapping/PublicAPI.Unshipped.txt` (RS0016 and RS0017 are errors). A new generator
  diagnostic needs its entry in `src/ZeroAlloc.Mapping.Generator/AnalyzerReleases.Unshipped.md` and a row in
  `docs/diagnostics.md`, which states the diagnostic count. Do not move entries to the `Shipped` files by
  hand: the release workflow does it.
- Packing needs an externally supplied version (`-p:Version` or `-p:PackageVersion`), or `Directory.Build.props`
  fails with ZAVERSION001. When packing with `--no-build`, the build step must have been given
  `-p:Version` too, or ZAVERSION002 fires because the assembly carries another version.
- Allocation budgets are asserted in `tests/ZeroAlloc.Mapping.Tests` (`AllocationBudgetTests`,
  `NestedCollectionBudgetTests`). A flat record copy is 72 B and update-in-place is 0 B. A generator change
  that raises either is a regression, not a number to update.
- Every generator change is covered twice: an emit snapshot in the generator tests and, where the output
  runs, a behaviour test among the runtime tests. `IncrementalityTests` guards the generator's caching;
  keep the pipeline models equatable (`EquatableArray`).
- Coverage is gated at 80 percent line coverage; the `Tests`, `Benchmarks` and `ZeroAlloc.TestHelpers`
  modules are excluded in `coverage.runsettings`.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
