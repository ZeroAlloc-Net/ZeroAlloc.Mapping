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

$reports = Get-ChildItem -Path $resultsDir -Filter '*-report-github.md' | Sort-Object Name
if ($reports.Count -eq 0) {
    throw "No -report-github.md files found in $resultsDir."
}

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

# Use a MatchEvaluator delegate to avoid `$`-substitution issues with table content.
$evaluator = [System.Text.RegularExpressions.MatchEvaluator] { param($m) $wrapped }
$updated = [regex]::Replace($md, $pattern, $evaluator)

Set-Content -Path $perfMd -Value $updated -NoNewline

Write-Host "Imported $($reports.Count) benchmark reports into $perfMd."
