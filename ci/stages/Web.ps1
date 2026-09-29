#Requires -Version 7
<#
.SYNOPSIS Stage 2 — web UI: static analysis (imports, references, no network URLs, UI↔C#↔mock bridge
  command cross-check, budgets) and unit tests (node:test). Needs Node.js ≥ 20; runs on any OS.
#>
param()
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
Start-Stage 'Web'
$root = Get-RepoRoot
Push-Location $root
try {
    $node = Get-Command node -ErrorAction SilentlyContinue
    Add-Check 'Node.js available' ([bool]$node) $(if ($node) { (& node --version) } else { 'install Node.js ≥ 20' })
    if ($node) {
        $major = [int]((& node --version).TrimStart('v').Split('.')[0])
        Add-Check 'Node.js ≥ 20' ($major -ge 20) "v$major"
        Invoke-Checked 'web static analysis (ci/web/lint.mjs)' { node ci/web/lint.mjs } | Out-Null
        $reports = Join-Path $root 'dist' 'reports'
        New-Item -ItemType Directory -Force $reports | Out-Null
        # spec reporter for humans + junit for the CI summary
        Invoke-Checked 'web unit tests (node --test tests/web)' {
            node --test --test-reporter=spec --test-reporter-destination=stdout `
                --test-reporter=junit --test-reporter-destination=(Join-Path $reports 'web-tests.xml') tests/web/
        } | Out-Null
    }
}
finally { Pop-Location }
exit (Complete-Stage)
