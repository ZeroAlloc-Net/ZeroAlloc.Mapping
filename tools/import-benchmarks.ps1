#requires -Version 7
<#
.SYNOPSIS
  Splices BenchmarkDotNet result tables into docs/performance.md.

.DESCRIPTION
  Reads every *-report-github.md under
  benchmarks/ZeroAlloc.Mapping.Benchmarks/BenchmarkDotNet.Artifacts/results/
  and concatenates them into a single block, sandwiched between the
  <!-- BENCH:START --> and <!-- BENCH:END --> sentinels in performance.md.

  Only the Method, Ratio and Allocated columns are kept: the org publishes exact
  allocations and same-run ratios, never absolute timings, because timings drift
  between runs and machines while a ratio measured side by side holds.

  It stops instead of writing when a row reports no allocation although Gen0
  collections happened during it. BenchmarkDotNet has reported such false zeros
  for these benchmarks; check the row with a per-thread measurement first.
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

function Split-Row {
    param([string]$line)
    return @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
}

$keep = @('Method', 'Ratio', 'Allocated')
$suspect = @()
$environment = $null

$blocks = foreach ($r in $reports) {
    $title = Get-ScenarioName $r.BaseName
    $lines = Get-Content $r.FullName
    if (-not $environment) {
        $fence = @($lines | Select-String -Pattern '^```' | ForEach-Object { $_.LineNumber })
        if ($fence.Count -ge 2) { $environment = ($lines[($fence[0] - 1)..($fence[1] - 1)] -join "`n") }
    }

    $table = @($lines | Where-Object { $_.StartsWith('|') })
    $header = Split-Row $table[0]
    $indexes = @($keep | ForEach-Object { [array]::IndexOf($header, $_) } | Where-Object { $_ -ge 0 })
    $gen0 = [array]::IndexOf($header, 'Gen0')
    $alloc = [array]::IndexOf($header, 'Allocated')

    $out = @('| ' + (($indexes | ForEach-Object { $header[$_] }) -join ' | ') + ' |')
    $out += '|' + (($indexes | ForEach-Object { if ($header[$_] -eq 'Method') { '---' } else { '---:' } }) -join '|') + '|'
    foreach ($row in $table[2..($table.Count - 1)]) {
        $cells = Split-Row $row
        if ($gen0 -ge 0 -and $alloc -ge 0 -and $cells[$gen0] -notin @('-', '0', '0.0000', '') -and $cells[$alloc] -in @('-', '0 B')) {
            $suspect += "$title $($cells[0])"
        }
        $out += '| ' + (($indexes | ForEach-Object { $cells[$_] }) -join ' | ') + ' |'
    }
    "### $title`n`n$($out -join "`n")`n"
}

if ($suspect.Count -gt 0) {
    throw "Rows report no allocation although Gen0 collections happened: $($suspect -join ', '). Measure them per thread before publishing."
}

$content = $blocks -join "`n"
$timestamp = (Get-Date).ToString('yyyy-MM-dd')
$note = '> Allocations are exact. Speed is shown as same-run ratios against the hand-written mapper; absolute timings are not published.'
$wrapped = "<!-- BENCH:START -->`n_Last refreshed: ${timestamp}_`n`n$environment`n`n$note`n`n$content`n<!-- BENCH:END -->"

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
