<#
.SYNOPSIS
  OWNER: lead. Compile-check (and optionally test) into a PRIVATE output folder so parallel agents never
  overwrite each other's dist/. Use this during development instead of build.ps1.
.EXAMPLE
  powershell -NoProfile -File tools/check.ps1 -Name lib -Test
  powershell -NoProfile -File tools/check.ps1 -Name pcgw -Test -Filter Pcgw   # only report errors in paths matching "Pcgw"
#>
param([string]$Name = "dev", [switch]$Test, [string]$Filter = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $env:TEMP "gameshub-check\$Name"
New-Item -ItemType Directory -Force $out | Out-Null
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdk = (& dotnet --list-sdks | Select-Object -Last 1) -replace '^(\S+) \[(.+)\]$', '$2\$1'
$csc = Join-Path $sdk 'Roslyn\bincore\csc.dll'
$refs = 'mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions',
        'System.Net.Http','System.Xml','System.Xml.Linq','Microsoft.CSharp','System.Runtime.Serialization',
        'System.Management','System.IO.Compression','System.IO.Compression.FileSystem' | ForEach-Object { "-r:$fw\$_.dll" }
$refs += "-r:$root\lib\Microsoft.Web.WebView2.Core.dll", "-r:$root\lib\Microsoft.Web.WebView2.WinForms.dll"
$common = @('-nologo','-nostdlib','-langversion:latest','-warn:4','-nowarn:1591,0067,0169,0414,0649','-utf8output')

function Compile($argv, $label) {
    $o = & dotnet $csc @argv 2>&1
    $errs = $o | Where-Object { $_ -match ': error ' }
    if ($Filter) { $mine = $errs | Where-Object { $_ -match $Filter } } else { $mine = $errs }
    $o | Where-Object { $_ -match ': (error|warning) ' -and (-not $Filter -or $_ -match $Filter) } | ForEach-Object { Write-Host $_ }
    Write-Host "$label : $($errs.Count) error(s) total, $($mine.Count) matching filter" -ForegroundColor Cyan
    return ($LASTEXITCODE -eq 0)
}

$src = Get-ChildItem "$root\src\GamesHub" -Recurse -Filter *.cs | ForEach-Object FullName
$ok = Compile ($common + $refs + @('-target:winexe', "-out:$out\GamesHub.exe") + $src) "app"
if ($Test -and $ok) {
    $tsrc = Get-ChildItem "$root\tests" -Recurse -Filter *.cs | ForEach-Object FullName
    $ok = Compile ($common + $refs + @('-target:exe', "-r:$out\GamesHub.exe", "-out:$out\GamesHub.Tests.exe") + $tsrc) "tests"
    if ($ok) {
        Copy-Item "$root\lib\*.dll" $out -Force
        & "$out\GamesHub.Tests.exe"
    }
}
