# Shared helpers for the pipeline stages (PowerShell 7+, Windows and Linux).
# Every stage records checks with Add-Check; failures are collected, never thrown mid-stage, so one run
# reports everything that is wrong. Stages end with Complete-Stage, which returns the exit code.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:IsCi = $env:GITHUB_ACTIONS -eq 'true'
$script:Checks = [System.Collections.Generic.List[object]]::new()
$script:StageName = ''
$script:StageClock = $null

function Get-RepoRoot { $script:RepoRoot }
function Test-Ci { $script:IsCi }

function Start-Stage([string]$Name) {
    $script:StageName = $Name
    $script:Checks.Clear()
    $script:StageClock = [Diagnostics.Stopwatch]::StartNew()
    Write-Host ''
    Write-Host ("━━ {0} " -f $Name).PadRight(78, '━') -ForegroundColor Cyan
}

<#
.SYNOPSIS Records one check. -File/-Line produce a GitHub annotation on failure.
#>
function Add-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Passed,
        [string]$Detail = '',
        [string]$File = '',
        [int]$Line = 0,
        [switch]$Warning   # a failed warning-level check is reported but does not fail the stage
    )
    $level = if ($Passed) { 'ok' } elseif ($Warning) { 'warn' } else { 'fail' }
    $script:Checks.Add([pscustomobject]@{ Name = $Name; Level = $level; Detail = $Detail; File = $File; Line = $Line })
    $color = @{ ok = 'Green'; warn = 'Yellow'; fail = 'Red' }[$level]
    $tag = @{ ok = '  ok  '; warn = ' warn '; fail = ' FAIL ' }[$level]
    $where = if ($File) { " [$File$(if ($Line) { ":$Line" })]" } else { '' }
    Write-Host "$tag $Name$where$(if ($Detail) { " — $Detail" })" -ForegroundColor $color
    if ($script:IsCi -and -not $Passed) {
        $kind = if ($Warning) { 'warning' } else { 'error' }
        $loc = if ($File) { " file=$File$(if ($Line) { ",line=$Line" })" } else { '' }
        $msg = "$Name$(if ($Detail) { ": $Detail" })" -replace "`r?`n", ' '
        Write-Host "::$kind$loc,title=$script:StageName::$msg"
    }
}

<#
.SYNOPSIS Runs an external command, streams its output, records a check from its exit code.
#>
function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Command
    )
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $global:LASTEXITCODE = 0
    $failed = $false
    try { & $Command } catch { $failed = $true; Write-Host $_ -ForegroundColor Red }
    $code = $global:LASTEXITCODE
    $ok = -not $failed -and $code -eq 0
    Add-Check -Name $Name -Passed $ok -Detail ("{0:0.0}s{1}" -f $sw.Elapsed.TotalSeconds, $(if (-not $ok) { ", exit $code" } else { '' }))
    return $ok
}

function Complete-Stage {
    $fail = @($script:Checks | Where-Object Level -eq 'fail').Count
    $warn = @($script:Checks | Where-Object Level -eq 'warn').Count
    $ok = @($script:Checks | Where-Object Level -eq 'ok').Count
    $secs = $script:StageClock.Elapsed.TotalSeconds
    $status = if ($fail) { 'FAILED' } else { 'PASSED' }
    $color = if ($fail) { 'Red' } else { 'Green' }
    Write-Host ("{0}: {1} — {2} ok, {3} warnings, {4} failed ({5:0.0}s)" -f $script:StageName, $status, $ok, $warn, $fail, $secs) -ForegroundColor $color

    $reports = Join-Path $script:RepoRoot 'dist' 'reports'
    New-Item -ItemType Directory -Force $reports | Out-Null
    $slug = ($script:StageName -replace '[^A-Za-z0-9]+', '-').ToLowerInvariant().Trim('-')
    $script:Checks | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $reports "stage-$slug.json") -Encoding utf8

    if ($env:GITHUB_STEP_SUMMARY) {
        $icon = if ($fail) { '❌' } elseif ($warn) { '⚠️' } else { '✅' }
        $lines = @("### $icon $($script:StageName) — $ok ok, $warn warnings, $fail failed ($([math]::Round($secs, 1))s)")
        $problems = @($script:Checks | Where-Object Level -ne 'ok')
        if ($problems.Count) {
            $lines += '', '| | Check | Detail |', '|---|---|---|'
            foreach ($c in $problems) {
                $mark = if ($c.Level -eq 'fail') { '❌' } else { '⚠️' }
                $loc = if ($c.File) { " (`$($c.File)$(if ($c.Line) { ":$($c.Line)" })`)" } else { '' }
                $lines += "| $mark | $($c.Name)$loc | $(($c.Detail -replace '\|', '\|') -replace "`r?`n", ' ') |"
            }
        }
        Add-Content $env:GITHUB_STEP_SUMMARY ($lines -join "`n") -Encoding utf8
    }
    if ($fail) { return 1 } else { return 0 }
}

function Get-AppVersion {
    $info = Get-Content (Join-Path $script:RepoRoot 'src/GamesHub/Properties/AssemblyInfo.cs') -Raw
    $m = [regex]::Match($info, 'AssemblyInformationalVersion\("([^"]+)"\)')
    if (-not $m.Success) { throw 'AssemblyInformationalVersion not found' }
    return $m.Groups[1].Value
}

function Get-TrackedFiles {
    Push-Location $script:RepoRoot
    # Tracked + new (not ignored) files: locally, files not yet committed are checked too.
    try { return @(git ls-files -z --cached --others --exclude-standard | ForEach-Object { $_ -split "`0" } | Where-Object { $_ } | Sort-Object -Unique) }
    finally { Pop-Location }
}
