<#
.SYNOPSIS
  Builds GamesHub. OWNER: DIST agent (packaging stage); others may only run it.
.EXAMPLE
  ./build.ps1            # app -> dist/app
  ./build.ps1 -Test      # app + tests, runs tests
  ./build.ps1 -Package   # app + installer -> dist/package
#>
param(
    [switch]$Test,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$appOut = Join-Path $dist 'app'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

# Modern Roslyn (from the .NET SDK) compiling against the .NET Framework 4.8 runtime assemblies:
# modern C# syntax, tiny exe, no runtime to ship.
$sdk = (& dotnet --list-sdks | Select-Object -Last 1) -replace '^(\S+) \[(.+)\]$', '$2\$1'
$csc = Join-Path $sdk 'Roslyn\bincore\csc.dll'
if (-not (Test-Path $csc)) { throw "Roslyn csc.dll not found at $csc (install the .NET SDK)" }

$fwRefs = 'mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions',
          'System.Net.Http','System.Xml','System.Xml.Linq','Microsoft.CSharp','System.Runtime.Serialization' |
          ForEach-Object { "-r:$fw\$_.dll" }
$wvRefs = "-r:$root\lib\Microsoft.Web.WebView2.Core.dll", "-r:$root\lib\Microsoft.Web.WebView2.WinForms.dll"
$common = @('-nologo', '-nostdlib', '-langversion:latest', '-optimize+', '-deterministic', '-warn:4',
            '-nowarn:1591,0067,0169,0414,0649', '-utf8output', '-platform:anycpu')

function Invoke-Csc([string[]]$argv) {
    & dotnet $csc @argv
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }
}

# ---------------------------------------------------------------- app
New-Item -ItemType Directory -Force $appOut | Out-Null
$src = Get-ChildItem (Join-Path $root 'src\GamesHub') -Recurse -Filter *.cs | ForEach-Object FullName
Write-Host "Compiling GamesHub.exe ($($src.Count) files)..." -ForegroundColor Cyan
Invoke-Csc ($common + $fwRefs + $wvRefs + @('-target:winexe', "-win32icon:$root\assets\icons\gamehub-app.ico",
    "-out:$appOut\GamesHub.exe") + $src)

Copy-Item "$root\lib\*.dll" $appOut -Force
Copy-Item "$root\assets\icons\*.ico" $appOut -Force
if (Test-Path "$appOut\web") { Remove-Item "$appOut\web" -Recurse -Force }
Copy-Item "$root\web" "$appOut\web" -Recurse -Force
Write-Host "OK -> $appOut\GamesHub.exe" -ForegroundColor Green

# ---------------------------------------------------------------- tests
if ($Test) {
    $testOut = Join-Path $dist 'tests'
    New-Item -ItemType Directory -Force $testOut | Out-Null
    $tsrc = Get-ChildItem (Join-Path $root 'tests') -Recurse -Filter *.cs | ForEach-Object FullName
    Write-Host "Compiling tests ($($tsrc.Count) files)..." -ForegroundColor Cyan
    Invoke-Csc ($common + $fwRefs + $wvRefs + @('-target:exe', "-r:$appOut\GamesHub.exe",
        "-out:$testOut\GamesHub.Tests.exe") + $tsrc)
    Copy-Item "$appOut\GamesHub.exe", "$appOut\*.dll" $testOut -Force
    & "$testOut\GamesHub.Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw "$LASTEXITCODE test(s) failed" }
}

# ---------------------------------------------------------------- package
if ($Package) {
    $pkg = Join-Path $dist 'package'
    if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
    New-Item -ItemType Directory -Force $pkg | Out-Null
    Copy-Item "$appOut\*" $pkg -Recurse -Force
    foreach ($p in 'Setup','Uninstall') {
        $icon = if ($p -eq 'Setup') { 'gamehub-installer.ico' } else { 'gamehub-uninstaller.ico' }
        Write-Host "Compiling $p.exe..." -ForegroundColor Cyan
        Invoke-Csc ($common + $fwRefs + @('-target:winexe', "-win32icon:$root\assets\icons\$icon",
            "$root\src\GamesHub\Properties\AssemblyInfo.cs", "-out:$pkg\$p.exe", "$root\src\Installer\$p.cs"))
    }
    Write-Host "OK -> $pkg" -ForegroundColor Green
}
