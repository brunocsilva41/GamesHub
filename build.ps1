<#
.SYNOPSIS
  Builds GamesHub. .EXAMPLE
  ./build.ps1                    # app -> dist/app
  ./build.ps1 -Test              # app + tests, runs tests
  ./build.ps1 -Package           # app + Uninstall.exe + single-file installer -> dist/package
  ./build.ps1 -Clean -Test -Package
#>
param(
    [switch]$Test,
    [switch]$Package,
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$appOut = Join-Path $dist 'app'
$objOut = Join-Path $dist 'obj'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

if ($Clean -and (Test-Path $dist)) {
    Write-Host "Cleaning $dist..." -ForegroundColor DarkGray
    Remove-Item $dist -Recurse -Force
}

# Version: single source of truth is src/GamesHub/Properties/AssemblyInfo.cs
$asmInfo = Join-Path $root 'src\GamesHub\Properties\AssemblyInfo.cs'
$m = [regex]::Match((Get-Content $asmInfo -Raw), 'AssemblyInformationalVersion\("([^"]+)"\)')
if (-not $m.Success) { throw "AssemblyInformationalVersion not found in $asmInfo" }
$version = $m.Groups[1].Value
Write-Host "GamesHub $version" -ForegroundColor White

# Modern Roslyn (from the .NET SDK) compiling against the .NET Framework 4.8 runtime assemblies:
# modern C# syntax, tiny exe, no runtime to ship.
$sdk = (& dotnet --list-sdks | Select-Object -Last 1) -replace '^(\S+) \[(.+)\]$', '$2\$1'
$csc = Join-Path $sdk 'Roslyn\bincore\csc.dll'
if (-not (Test-Path $csc)) { throw "Roslyn csc.dll not found at $csc (install the .NET SDK)" }

$fwRefs = 'mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions',
          'System.Net.Http','System.Xml','System.Xml.Linq','Microsoft.CSharp','System.Runtime.Serialization',
          'System.Management','System.IO.Compression','System.IO.Compression.FileSystem' |
          ForEach-Object { "-r:$fw\$_.dll" }
$wvRefs = "-r:$root\lib\Microsoft.Web.WebView2.Core.dll", "-r:$root\lib\Microsoft.Web.WebView2.WinForms.dll"
$common = @('-nologo', '-nostdlib', '-langversion:latest', '-optimize+', '-deterministic', '-warn:4',
            '-nowarn:1591,0067,0169,0414,0649', '-utf8output', '-platform:anycpu')

function Invoke-Csc([string[]]$argv) {
    & dotnet $csc @argv
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }
}
function Get-Sources([string]$dir) { Get-ChildItem (Join-Path $root $dir) -Recurse -Filter *.cs | ForEach-Object FullName }
function Format-Size([long]$b) { if ($b -ge 1MB) { '{0:N1} MB' -f ($b / 1MB) } else { '{0:N0} KB' -f ($b / 1KB) } }

# ---------------------------------------------------------------- app
New-Item -ItemType Directory -Force $appOut, $objOut | Out-Null
$src = Get-Sources 'src\GamesHub'
Write-Host "Compiling GamesHub.exe ($($src.Count) files)..." -ForegroundColor Cyan
Invoke-Csc ($common + $fwRefs + $wvRefs + @('-target:winexe', "-win32icon:$root\assets\icons\gamehub-app.ico",
    "-win32manifest:$root\assets\manifests\app.manifest", "-out:$appOut\GamesHub.exe") + $src)

Copy-Item "$root\lib\*.dll", "$root\lib\WebView2-LICENSE.txt" $appOut -Force
Copy-Item "$root\assets\GamesHub.exe.config" $appOut -Force
Copy-Item "$root\assets\icons\*.ico" $appOut -Force
Remove-Item "$appOut\gamehub-installer.ico" -ErrorAction SilentlyContinue   # only needed inside Setup.exe
if (Test-Path "$appOut\web") { Remove-Item "$appOut\web" -Recurse -Force }
Copy-Item "$root\web" "$appOut\web" -Recurse -Force
Write-Host "OK -> $appOut\GamesHub.exe" -ForegroundColor Green

# ---------------------------------------------------------------- tests
if ($Test) {
    $testOut = Join-Path $dist 'tests'
    New-Item -ItemType Directory -Force $testOut | Out-Null
    $tsrc = Get-Sources 'tests'
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

    # Installer assemblies get their own version info (same version, different title)
    $v4 = ($version -replace '[-+].*$', '') ; while (($v4.Split('.')).Count -lt 4) { $v4 += '.0' }
    $infoFile = Join-Path $objOut 'InstallerInfo.cs'
    $refs = $fwRefs
    $installerCommon = Get-Sources 'src\Installer\Common'
    $manifest = "-win32manifest:$root\assets\manifests\installer.manifest"
    function Write-InstallerInfo([string]$title) {
        @"
using System.Reflection;
[assembly: AssemblyTitle("$title")]
[assembly: AssemblyProduct("GamesHub")]
[assembly: AssemblyCompany("Bruno Silva")]
[assembly: AssemblyCopyright("© 2026 Bruno Silva")]
[assembly: AssemblyVersion("$v4")]
[assembly: AssemblyFileVersion("$v4")]
[assembly: AssemblyInformationalVersion("$version")]
"@ | Set-Content $infoFile -Encoding UTF8
    }

    # 1. Uninstall.exe goes into the app folder, so it is part of the payload
    Write-Host "Compiling Uninstall.exe..." -ForegroundColor Cyan
    Write-InstallerInfo 'Desinstalador do GamesHub'
    Invoke-Csc ($common + $refs + @('-target:winexe', "-win32icon:$root\assets\icons\gamehub-uninstaller.ico", $manifest,
        "-out:$appOut\Uninstall.exe", $infoFile) + $installerCommon + (Get-Sources 'src\Installer\Uninstall'))

    # 2. payload.zip = dist/app
    $payload = Join-Path $objOut 'payload.zip'
    if (Test-Path $payload) { Remove-Item $payload -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Write-Host "Zipping payload..." -ForegroundColor Cyan
    [IO.Compression.ZipFile]::CreateFromDirectory($appOut, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

    # 3. Setup.exe with the payload embedded as a manifest resource
    $setupExe = Join-Path $pkg "GamesHub-Setup-$version.exe"
    Write-Host "Compiling $(Split-Path $setupExe -Leaf)..." -ForegroundColor Cyan
    Write-InstallerInfo 'Instalador do GamesHub'
    Invoke-Csc ($common + $refs + @('-target:winexe', "-win32icon:$root\assets\icons\gamehub-installer.ico", $manifest,
        "-resource:$payload,GamesHub.Payload.zip", "-out:$setupExe", $infoFile) + $installerCommon + (Get-Sources 'src\Installer\Setup'))

    # 4. checksum (sha256sum format; the updater verifies it when published next to the exe)
    $sha = [Security.Cryptography.SHA256]::Create()
    $fs = [IO.File]::OpenRead($setupExe)
    try { $hash = -join ($sha.ComputeHash($fs) | ForEach-Object { $_.ToString('x2') }) } finally { $fs.Dispose(); $sha.Dispose() }
    "$hash  $(Split-Path $setupExe -Leaf)" | Set-Content "$setupExe.sha256" -Encoding ASCII -NoNewline

    $appSize = (Get-ChildItem $appOut -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ""
    Write-Host "Package summary" -ForegroundColor White
    Write-Host ("  app folder     {0,10}  ({1} files)" -f (Format-Size $appSize), (Get-ChildItem $appOut -Recurse -File).Count)
    Write-Host ("  payload.zip    {0,10}" -f (Format-Size (Get-Item $payload).Length))
    Write-Host ("  Setup          {0,10}  {1}" -f (Format-Size (Get-Item $setupExe).Length), $setupExe)
    Write-Host ("  SHA-256        {0}" -f $hash)
    Write-Host "OK -> $pkg" -ForegroundColor Green
}
Write-Host ("Done in {0:N1}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray
