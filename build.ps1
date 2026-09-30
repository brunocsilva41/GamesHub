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
    [switch]$Clean,
    # CI: compiler warnings fail the build.
    [switch]$Strict,
    # Optional JUnit XML report for the unit tests (CI test summary).
    [string]$JUnit = ''
)
$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$appOut = Join-Path $dist 'app'
$objOut = Join-Path $dist 'obj'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

if ($Clean -and (Test-Path $dist)) {
    Write-Host "Cleaning $dist (keeping reports/ and e2e/)..." -ForegroundColor DarkGray
    Get-ChildItem $dist -Force | Where-Object { $_.Name -notin 'reports', 'e2e' } | Remove-Item -Recurse -Force
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
if ($Strict) { $common += '-warnaserror+' }
# Source paths are mapped to /_/ so the same commit builds byte-identical binaries on any machine.
$common += "-pathmap:$root=/_/"

function Invoke-Csc([string[]]$argv) {
    & dotnet $csc @argv
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }
}
function Get-Sources([string]$dir) { Get-ChildItem (Join-Path $root $dir) -Recurse -Filter *.cs | ForEach-Object FullName }
function Format-Size([long]$b) { if ($b -ge 1MB) { '{0:N1} MB' -f ($b / 1MB) } else { '{0:N0} KB' -f ($b / 1KB) } }

# Optional Authenticode signing: active only when a certificate is provided through the environment
# (CI secrets GAMESHUB_SIGN_PFX_BASE64 + GAMESHUB_SIGN_PFX_PASSWORD). Unsigned builds are the default.
$script:SignPfx = $null
function Invoke-Sign([string]$file) {
    if (-not $env:GAMESHUB_SIGN_PFX_BASE64) { return }
    if (-not $script:SignPfx) {
        $script:SignPfx = Join-Path ([IO.Path]::GetTempPath()) ("gh-sign-" + [guid]::NewGuid() + ".pfx")
        [IO.File]::WriteAllBytes($script:SignPfx, [Convert]::FromBase64String($env:GAMESHUB_SIGN_PFX_BASE64))
    }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found (Windows SDK)' }
    & $signtool.FullName sign /fd SHA256 /f $script:SignPfx /p $env:GAMESHUB_SIGN_PFX_PASSWORD `
        /tr 'http://timestamp.digicert.com' /td SHA256 /d 'GamesHub' $file | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $file" }
    Write-Host "Signed $(Split-Path $file -Leaf)" -ForegroundColor Green
}

# ---------------------------------------------------------------- app
New-Item -ItemType Directory -Force $appOut, $objOut | Out-Null
$src = Get-Sources 'src\GamesHub'

# Built-in SteamGridDB API key (optional). It never lives in the repository: CI passes it through the
# GAMESHUB_SGDB_KEY secret, local builds read .local/sgdb.key (git-ignored). It is XOR-masked so the binary
# does not carry it as a plain string; without a key the app simply relies on the user's own (Settings).
$sgdbKey = $env:GAMESHUB_SGDB_KEY
$localKey = Join-Path $root '.local\sgdb.key'
if (-not $sgdbKey -and (Test-Path $localKey)) { $sgdbKey = (Get-Content $localKey -Raw).Trim() }
if ($sgdbKey -and $sgdbKey -notmatch '^[0-9a-fA-F]{32}$') { throw 'GAMESHUB_SGDB_KEY must be a 32-character hex SteamGridDB API key' }
$mask = [Text.Encoding]::ASCII.GetBytes('GamesHub.Art')
$masked = if ($sgdbKey) {
    $b = [Text.Encoding]::ASCII.GetBytes($sgdbKey)
    [Convert]::ToBase64String([byte[]](0..($b.Length - 1) | ForEach-Object { $b[$_] -bxor $mask[$_ % $mask.Length] }))
} else { '' }
$secretsFile = Join-Path $objOut 'BuildSecrets.g.cs'
@"
// <auto-generated> by build.ps1 — do not edit, do not commit.
namespace GamesHub
{
    internal static class BuildSecrets
    {
        private const string SgdbMasked = "$masked";
        /// <summary>SteamGridDB key embedded at build time ("" when the build had none).</summary>
        internal static string SteamGridDbKey
        {
            get
            {
                if (SgdbMasked.Length == 0) return "";
                byte[] b = System.Convert.FromBase64String(SgdbMasked), m = System.Text.Encoding.ASCII.GetBytes("GamesHub.Art");
                for (int i = 0; i < b.Length; i++) b[i] ^= m[i % m.Length];
                return System.Text.Encoding.ASCII.GetString(b);
            }
        }
    }
}
"@ | Set-Content $secretsFile -Encoding UTF8
$src = @($src) + $secretsFile
Write-Host ("SteamGridDB built-in key: {0}" -f $(if ($sgdbKey) { 'embedded' } else { 'none (user key only)' })) -ForegroundColor DarkGray
Write-Host "Compiling GamesHub.exe ($($src.Count) files)..." -ForegroundColor Cyan
Invoke-Csc ($common + $fwRefs + $wvRefs + @('-target:winexe', "-win32icon:$root\assets\icons\gamehub-app.ico",
    "-win32manifest:$root\assets\manifests\app.manifest", "-out:$appOut\GamesHub.exe") + $src)

Copy-Item "$root\lib\*.dll", "$root\lib\WebView2-LICENSE.txt" $appOut -Force
Copy-Item "$root\assets\GamesHub.exe.config" $appOut -Force
Copy-Item "$root\assets\icons\*.ico" $appOut -Force
Remove-Item "$appOut\gamehub-installer.ico" -ErrorAction SilentlyContinue   # only needed inside Setup.exe
if (Test-Path "$appOut\web") { Remove-Item "$appOut\web" -Recurse -Force }
Copy-Item "$root\web" "$appOut\web" -Recurse -Force
Get-ChildItem "$appOut\web" -Recurse -Filter *.md | Remove-Item -Force   # developer docs don't ship
Invoke-Sign "$appOut\GamesHub.exe"
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
    if ($JUnit) { & "$testOut\GamesHub.Tests.exe" --junit $JUnit } else { & "$testOut\GamesHub.Tests.exe" }
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
    Invoke-Sign "$appOut\Uninstall.exe"

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
    Invoke-Sign $setupExe

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
if ($script:SignPfx -and (Test-Path $script:SignPfx)) { Remove-Item $script:SignPfx -Force }
Write-Host ("Done in {0:N1}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray
