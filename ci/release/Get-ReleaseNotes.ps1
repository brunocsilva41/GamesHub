#Requires -Version 7
<#
.SYNOPSIS Writes the GitHub Release notes: the CHANGELOG section of the version + download verification help.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$OutFile
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$esc = [regex]::Escape($Version)
$m = [regex]::Match($changelog, "(?ms)^## \[$esc\] - \d{4}-\d{2}-\d{2}\s*$(.*?)(?=^## \[|\z)")
if (-not $m.Success) { throw "CHANGELOG has no section for $Version" }
$body = ($m.Groups[1].Value -replace '(?m)^\[[^\]]+\]:.*$', '').Trim()

$name = Split-Path $Installer -Leaf
$hash = (Get-FileHash $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
$repo = $env:GITHUB_REPOSITORY ?? 'brunocsilva41/GamesHub'
$signed = (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue) -and
          (Get-AuthenticodeSignature $Installer).Status -eq 'Valid'

$notes = @"
$body

---

### Download

Baixe **``$name``** abaixo e execute. Não precisa de permissão de administrador; o GamesHub é instalado só para o seu usuário.
Requisitos: Windows 10 ou 11 (o WebView2 e o .NET Framework 4.8 já vêm no Windows 11).

### Verificar o download

SHA-256: ``$hash``

``````powershell
Get-FileHash .\$name -Algorithm SHA256
``````

Este arquivo foi gerado pelo pipeline público do repositório, com uma **atestação de procedência** (build provenance) assinada pelo GitHub:

``````powershell
gh attestation verify .\$name --repo $repo
``````
$(if (-not $signed) { "`n> O instalador ainda não tem assinatura digital de código; o Windows SmartScreen pode exibir um aviso. Clique em **Mais informações › Executar assim mesmo**, depois de conferir o SHA-256 acima." })
"@
Set-Content $OutFile $notes -Encoding utf8
Write-Host "Release notes written to $OutFile ($((Get-Item $OutFile).Length) bytes)"
