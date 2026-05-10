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
    throw "No results at $resultsDir. Run 'dotnet run -c Release --project benchmarks/ZeroAlloc.Mapping.Benchmarks -- --filter `"*`"' first."
}

# Explicit narrative ordering — alphabetical sort would scatter related rows
# (Collection→FlatConversion→FlatIdentity…). The intuitive reading order is
# the baseline first, then conversions, then increasingly composite scenarios.
$order = @('FlatIdentity', 'FlatConversion', 'Flattening', 'Collection', 'Polymorphic', 'UpdateInPlace', 'TryMap')

function Get-ScenarioName {
    param([string]$baseName)
    return ($baseName -replace '^ZeroAlloc\.Mapping\.Benchmarks\.Scenarios\.', '' -replace '-report-github$', '' -replace 'Bench$', '')
}

$reports = Get-ChildItem -Path $resultsDir -Filter '*-report-github.md' | Sort-Object {
    $name = Get-ScenarioName $_.BaseName
    $idx = $order.IndexOf($name)
    if ($idx -ge 0) { $idx } else { 99 }  # unknown scenarios sort last
}, Name
if ($reports.Count -eq 0) {
    throw "No -report-github.md files found in $resultsDir."
}

$blocks = foreach ($r in $reports) {
    $title = Get-ScenarioName $r.BaseName
    $body = Get-Content $r.FullName -Raw
    "### $title`n`n$body`n"
}

$content = $blocks -join "`n"
$timestamp = (Get-Date).ToString('yyyy-MM-dd')
$wrapped = "<!-- BENCH:START -->`n_Last refreshed: ${timestamp}_`n`n$content`n<!-- BENCH:END -->"

$md = Get-Content $perfMd -Raw
$pattern = '<!-- BENCH:START -->[\s\S]*?<!-- BENCH:END -->'
if ($md -notmatch $pattern) {
    throw "Sentinels not found in $perfMd. Add '<!-- BENCH:START -->' and '<!-- BENCH:END -->' before running."
}

# Use a MatchEvaluator delegate to avoid `$`-substitution issues with table content.
$evaluator = [System.Text.RegularExpressions.MatchEvaluator] { param($m) $wrapped }
$updated = [regex]::Replace($md, $pattern, $evaluator)

# Retry write — performance.md may be transiently locked by an editor / indexer / sync agent.
$attempts = 0
while ($true) {
    try {
        [System.IO.File]::WriteAllText($perfMd, $updated, [System.Text.UTF8Encoding]::new($false))
        break
    } catch [System.IO.IOException] {
        $attempts++
        if ($attempts -ge 10) { throw }
        Start-Sleep -Milliseconds 500
    }
}

Write-Host "Imported $($reports.Count) benchmark reports into $perfMd."
