#Requires -Version 7
<#
.SYNOPSIS Stage 6 — end-to-end smoke test.
  CI (clean runner): installs the built installer silently, runs the INSTALLED app and inspects it through the
  WebView2 DevTools protocol, upgrades over itself, uninstalls and verifies the machine is clean.
  Local: runs the built app in App mode only (isolated data folder + isolated single instance), never the installer.
#>
param([ValidateSet('Auto', 'App', 'Installer')][string]$Mode = 'Auto')
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
Start-Stage 'End-to-end'
$root = Get-RepoRoot
if ($Mode -eq 'Auto') { $Mode = if (Test-Ci) { 'Installer' } else { 'App' } }
Push-Location $root
try {
    $script = Join-Path $root 'ci/e2e/Invoke-E2E.ps1'
    Add-Check 'E2E harness present' (Test-Path $script)
    Add-Check 'built app present (run the Build stage first)' (Test-Path 'dist/app/GamesHub.exe')
    if ($Mode -eq 'Installer') {
        $setup = Join-Path $root "dist/package/GamesHub-Setup-$(Get-AppVersion).exe"
        Add-Check 'installer present (run the Package stage first)' (Test-Path $setup)
    }
    if ((Test-Path $script) -and (Test-Path 'dist/app/GamesHub.exe')) {
        $out = Join-Path $root 'dist/e2e'
        if ($Mode -eq 'Installer') {
            Invoke-Checked "E2E ($Mode mode)" { pwsh -NoProfile -File $script -Mode Installer -AppDir dist/app -Setup $setup -OutDir $out } | Out-Null
        } else {
            Invoke-Checked "E2E ($Mode mode)" { pwsh -NoProfile -File $script -Mode App -AppDir dist/app -OutDir $out } | Out-Null
        }
        $results = Join-Path $out 'e2e-results.json'
        if (Test-Path $results) {
            $r = @(Get-Content $results -Raw | ConvertFrom-Json -Depth 8)
            $e2eChecks = if ($r[0].PSObject.Properties.Name -contains 'checks') { @($r[0].checks) } else { $r }
            $failed = @($e2eChecks | Where-Object { -not $_.pass })
            Add-Check 'E2E result file' ($e2eChecks.Count -gt 0) "$($e2eChecks.Count) checks, $($failed.Count) failed"
            foreach ($f in $failed) { Add-Check "E2E: $($f.name)" $false "$($f.detail)" }
        } else {
            Add-Check 'E2E result file written' $false $results
        }
    }
}
finally { Pop-Location }
exit (Complete-Stage)
