#Requires -Version 7
<#
.SYNOPSIS
  GamesHub pipeline — the same stages CI runs, in order, with a final summary.

.DESCRIPTION
  1 Hygiene    secrets & personal data, encodings/line endings, pinned binaries, required files, JSON, links, versions
  2 Web        static analysis of the UI (imports, references, bridge command cross-check, budgets) + node:test suite
  3 Build      strict compile (warnings = errors), 250+ unit tests with JUnit report, reproducible-build check
  4 Package    single-file installer + SHA-256; payload, version info, dependencies and signature verification
  5 E2E        CI: install → run → inspect → upgrade → uninstall on a clean machine; locally: isolated app run

  Stops at the first failed stage unless -KeepGoing. Exit code 0 only if every selected stage passed.

.EXAMPLE
  pwsh ci/Invoke-Pipeline.ps1                      # everything (E2E in safe App mode locally)
  pwsh ci/Invoke-Pipeline.ps1 -Stage Hygiene,Web   # quick pre-commit check
  pwsh ci/Invoke-Pipeline.ps1 -SkipE2E -KeepGoing
#>
param(
    [ValidateSet('Hygiene', 'Web', 'Build', 'Package', 'E2E')]
    [string[]]$Stage = @('Hygiene', 'Web', 'Build', 'Package', 'E2E'),
    [switch]$SkipE2E,
    [switch]$KeepGoing
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$order = 'Hygiene', 'Web', 'Build', 'Package', 'E2E'
$selected = $order | Where-Object { $Stage -contains $_ -and -not ($SkipE2E -and $_ -eq 'E2E') }

$results = [ordered]@{}
$clock = [Diagnostics.Stopwatch]::StartNew()
foreach ($s in $selected) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $script = Join-Path $PSScriptRoot 'stages' "$s.ps1"
    # Package rebuilds from clean on purpose, so the installer never contains leftovers of earlier stages.
    & pwsh -NoProfile -File $script
    $code = $LASTEXITCODE
    $results[$s] = [pscustomobject]@{ Stage = $s; Passed = ($code -eq 0); Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
    if ($code -ne 0 -and -not $KeepGoing) { break }
}

Write-Host ''
Write-Host ('━' * 78) -ForegroundColor Cyan
Write-Host ' Pipeline summary' -ForegroundColor Cyan
foreach ($r in $results.Values) {
    $mark = if ($r.Passed) { 'PASS' } else { 'FAIL' }
    Write-Host ("  {0}  {1,-10} {2,6:0.0}s" -f $mark, $r.Stage, $r.Seconds) -ForegroundColor $(if ($r.Passed) { 'Green' } else { 'Red' })
}
foreach ($s in $selected | Where-Object { -not $results.Contains($_) }) {
    Write-Host ("  skip  {0}" -f $s) -ForegroundColor DarkGray
}
$allPassed = ($results.Values | Where-Object { -not $_.Passed }).Count -eq 0 -and $results.Count -eq @($selected).Count
Write-Host ("  total {0:0.0}s — {1}" -f $clock.Elapsed.TotalSeconds, $(if ($allPassed) { 'ALL STAGES PASSED' } else { 'PIPELINE FAILED' })) `
    -ForegroundColor $(if ($allPassed) { 'Green' } else { 'Red' })
exit $(if ($allPassed) { 0 } else { 1 })
