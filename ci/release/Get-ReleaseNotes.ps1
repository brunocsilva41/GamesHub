#Requires -Version 7
<#
.SYNOPSIS Writes the GitHub Release page for a version, from CHANGELOG.md:
  summary, changes grouped by type, download table (size + SHA-256), install steps, requirements, screenshots,
  verification and the full-changelog comparison link.
.EXAMPLE
  ./ci/release/Get-ReleaseNotes.ps1 -Version 2.0.2 -Installer dist/package/GamesHub-Setup-2.0.2.exe -OutFile notes.md
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$Repo = ($env:GITHUB_REPOSITORY ?? 'brunocsilva41/GamesHub')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$changelog = (Get-Content (Join-Path $root 'CHANGELOG.md') -Raw) -replace "`r`n", "`n"
$esc = [regex]::Escape($Version)
$m = [regex]::Match($changelog, "(?ms)^## \[$esc\] - (\d{4}-\d{2}-\d{2})\s*`$(.*?)(?=^## \[|\z)")
if (-not $m.Success) { throw "CHANGELOG has no section for $Version" }
$date = [datetime]::ParseExact($m.Groups[1].Value, 'yyyy-MM-dd', $null)
$body = ($m.Groups[2].Value -replace '(?m)^\[[^\]]+\]:.*$', '').Trim()

# Summary = text before the first "###" heading; the rest are the typed change lists.
$split = [regex]::Match($body, '(?ms)^(.*?)(?=^### |\z)(.*)$')
$summary = $split.Groups[1].Value.Trim()
$changes = $split.Groups[2].Value.Trim()
$titles = @{
    'Adicionado' = '✨ Novidades'; 'Alterado' = '🔧 Melhorias'; 'Corrigido' = '🐛 Correções'
    'Segurança' = '🔒 Segurança'; 'Removido' = '🗑️ Removido'; 'Obsoleto' = '⚠️ Obsoleto'
}
# Order sections by what users care about first.
$order = 'Adicionado', 'Alterado', 'Corrigido', 'Segurança', 'Removido', 'Obsoleto'
$sections = [regex]::Matches($changes, '(?ms)^### ([^\n]+)\n(.*?)(?=^### |\z)') |
    ForEach-Object { [pscustomobject]@{ Name = $_.Groups[1].Value.Trim(); Text = $_.Groups[2].Value.Trim() } } |
    Sort-Object { $i = [array]::IndexOf($order, $_.Name); if ($i -lt 0) { 99 } else { $i } }
# CHANGELOG items are hard-wrapped at ~120 columns; release pages turn every newline into a line break, so
# continuation lines are joined back into their bullet.
$changeMd = ($sections | ForEach-Object { "### $($titles[$_.Name] ?? $_.Name)`n`n$($_.Text -replace '\n[ \t]+(?=\S)', ' ')" }) -join "`n`n"
$summary = $summary -replace '\s*\n\s*', ' '
# Plain-text first line (no Markdown/HTML): apps up to 2.0.3 show the start of the notes verbatim in their update
# banner, so the page opens with the highlights (bold leads of the first section) before any markup.
$first = $sections | Select-Object -First 1
$leads = if ($first) { [regex]::Matches($first.Text, '(?m)^- \*\*(.+?)\*\*') | ForEach-Object { $_.Groups[1].Value.Trim().TrimEnd(':', '.') } | Select-Object -First 3 } else { @() }
$headline = if ($leads) { "$($titles[$first.Name] -replace '^\S+\s+', ''): $($leads -join ' · ')" } else { "Versão $Version do GamesHub" }

# Previous release (highest vX.Y.Z tag below this one) for the comparison link.
function Parse([string]$v) { $c, $p = $v.TrimStart('v') -split '-', 2; [pscustomobject]@{ Core = [version]$c; Pre = $p } }
$this = Parse $Version
$prev = git -C $root tag --list 'v*' | Where-Object { $_ -match '^v\d+\.\d+\.\d+(-.+)?$' } |
    Where-Object { $p = Parse $_; $p.Core -lt $this.Core -or ($p.Core -eq $this.Core -and $p.Pre -and -not $this.Pre) } |
    Sort-Object { Parse $_ | ForEach-Object Core } | Select-Object -Last 1
$compare = if ($prev) { "https://github.com/$Repo/compare/$prev...v$Version" } else { "https://github.com/$Repo/commits/v$Version" }

# Installer facts
$name = Split-Path $Installer -Leaf
$hash = (Get-FileHash $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
$size = '{0:N1} MB' -f ((Get-Item $Installer).Length / 1MB)
$signed = (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue) -and (Get-AuthenticodeSignature $Installer).Status -eq 'Valid'
$dl = "https://github.com/$Repo/releases/download/v$Version/$name"
$shot = { param($f) "https://raw.githubusercontent.com/$Repo/v$Version/docs/screenshots/$f" }
$months = 'janeiro','fevereiro','março','abril','maio','junho','julho','agosto','setembro','outubro','novembro','dezembro'
$dateText = "$($date.Day) de $($months[$date.Month - 1]) de $($date.Year)"

$notes = @"
$headline

**Versão $Version** · lançada em $dateText · [ver todas as mudanças]($compare)

<p align="center"><img src="$(& $shot 'biblioteca.png')" alt="Biblioteca do GamesHub" width="820"></p>

$(if ($summary) { "> $($summary -replace "`n", ' ')`n" })

## 📋 O que mudou

$changeMd

## ⬇️ Download

| Arquivo | Tamanho | SHA-256 |
|---|---|---|
| [**$name**]($dl) | $size | ``$($hash.Substring(0, 16))…`` |

1. Baixe **$name** (no fim desta página, em *Assets*, ou pelo link da tabela).
2. Execute e siga o instalador — não precisa de permissão de administrador; o GamesHub é instalado só para o seu usuário.
3. Seus jogos da Steam, Epic, Riot e Hydra aparecem sozinhos. Para atalhos avulsos, use a pasta de jogos escolhida na instalação.

**Já usa o GamesHub?** A atualização chega sozinha: o app avisa quando há versão nova. Suas configurações, horas jogadas e artes são mantidas.

**Requisitos:** Windows 10 ou 11 (64 bits) · WebView2 Runtime e .NET Framework 4.8 (já vêm no Windows 11; o instalador avisa se faltar algo).
$(if (-not $signed) { "`n> [!NOTE]`n> O instalador ainda não tem assinatura digital de código, então o Windows SmartScreen pode mostrar um aviso na primeira execução. Depois de conferir o SHA-256 (abaixo), clique em **Mais informações › Executar assim mesmo**.`n" })
<details>
<summary><b>📸 Capturas de tela</b></summary>

| Detalhes do jogo | Busca rápida |
|---|---|
| <img src="$(& $shot 'detalhes.png')" alt="Página de detalhes" width="420"> | <img src="$(& $shot 'busca-rapida.png')" alt="Busca rápida" width="420"> |

<img src="$(& $shot 'big-picture.png')" alt="Modo Big Picture" width="840">

</details>

<details>
<summary><b>🔒 Verificar o download</b></summary>

SHA-256 completo:

``````
$hash  $name
``````

``````powershell
Get-FileHash .\$name -Algorithm SHA256
``````

Este instalador foi compilado e testado pelo [pipeline público](https://github.com/$Repo/blob/main/docs/PIPELINE.md) do repositório — build com avisos tratados como erro, testes automatizados e instalação/desinstalação completa num Windows limpo — e tem uma **atestação de procedência** assinada pelo GitHub:

``````powershell
gh attestation verify .\$name --repo $Repo
``````

A atualização automática do app é ainda mais rígida: o GamesHub só executa o instalador baixado se a lista de SHA-256 (``$name.sha256``) tiver uma **assinatura digital válida** (``$name.sha256.sig``) da chave de atualização do projeto, cuja parte pública vem embutida no próprio app. Se algo faltar ou não conferir, nada é instalado.

</details>

---

[Guia do usuário](https://github.com/$Repo/blob/main/docs/USER_GUIDE.md) · [Relatar um problema](https://github.com/$Repo/issues/new/choose) · [Changelog completo](https://github.com/$Repo/blob/main/CHANGELOG.md)
"@
Set-Content $OutFile ($notes -replace "(`n){3,}", "`n`n") -Encoding utf8
Write-Host "Release notes written to $OutFile ($((Get-Item $OutFile).Length) bytes; previous release: $($prev ?? 'none'))"
