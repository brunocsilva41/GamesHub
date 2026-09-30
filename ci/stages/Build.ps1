#Requires -Version 7
<#
.SYNOPSIS Stages 3+4 — strict build (warnings are errors), reproducibility check, unit tests with JUnit report.
  Windows only (targets .NET Framework 4.8).
#>
param([switch]$SkipReproducibility)
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
Start-Stage 'Build & unit tests'
$root = Get-RepoRoot
Push-Location $root
try {
    Add-Check 'running on Windows' $IsWindows
    $sdk = & dotnet --list-sdks 2>$null
    Add-Check '.NET SDK (Roslyn compiler) available' ([bool]$sdk) (($sdk | Select-Object -Last 1) ?? 'install the .NET SDK 8')
    Add-Check '.NET Framework 4.8 reference assemblies' (Test-Path "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll")

    # Bundled third-party binaries: besides the pinned SHA-256 (Hygiene), each must carry a valid Authenticode
    # signature from Microsoft, so a replaced DLL with a re-pinned hash still cannot slip into the installer.
    $microsoft = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
    foreach ($dll in Get-ChildItem (Join-Path $root 'lib') -Filter *.dll) {
        $sig = Get-AuthenticodeSignature -LiteralPath $dll.FullName
        $subject = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '(unsigned)' }
        Add-Check "Authenticode: lib/$($dll.Name)" ($sig.Status -eq 'Valid' -and $subject -ceq $microsoft) "$($sig.Status), $subject" -File "lib/$($dll.Name)"
    }

    $junit = Join-Path $root 'dist' 'reports' 'unit-tests.xml'
    $built = Invoke-Checked 'strict build + unit tests (build.ps1 -Clean -Strict -Test)' {
        pwsh -NoProfile -File build.ps1 -Clean -Strict -Test -JUnit $junit
    }
    if ($built -and (Test-Path $junit)) {
        [xml]$x = Get-Content $junit
        $tests = [int]$x.testsuites.tests; $failures = [int]$x.testsuites.failures
        Add-Check 'unit test report' ($failures -eq 0 -and $tests -gt 0) "$tests tests, $failures failures"
        Add-Check 'unit test count did not regress' ($tests -ge 250) "$tests (minimum 250)"
    }

    # Deterministic compiler output: building the same sources twice must give byte-identical binaries,
    # so a published installer can be traced back to its commit.
    if ($built -and -not $SkipReproducibility) {
        $first = (Get-FileHash 'dist/app/GamesHub.exe' -Algorithm SHA256).Hash
        $ok = Invoke-Checked 'rebuild for reproducibility' { pwsh -NoProfile -File build.ps1 -Strict }
        if ($ok) {
            $second = (Get-FileHash 'dist/app/GamesHub.exe' -Algorithm SHA256).Hash
            Add-Check 'reproducible build (identical GamesHub.exe)' ($first -eq $second) "$($first.Substring(0, 12))… vs $($second.Substring(0, 12))…"
        }
    }

    if (Test-Path 'dist/app/GamesHub.exe') {
        $size = (Get-Item 'dist/app/GamesHub.exe').Length
        Add-Check 'GamesHub.exe size budget (≤ 2 MB)' ($size -le 2MB) ("{0:N0} KB" -f ($size / 1KB))
        $v = (Get-Item 'dist/app/GamesHub.exe').VersionInfo
        $expected = (Get-AppVersion) -replace '-.*$', ''
        Add-Check 'GamesHub.exe version info' ($v.FileVersion -like "$expected*") "FileVersion $($v.FileVersion), product '$($v.ProductName)'"
    }
}
finally { Pop-Location }
exit (Complete-Stage)
