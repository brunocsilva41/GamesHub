#Requires -Version 7
<#
.SYNOPSIS
  Cuts a GamesHub release: bumps the version, dates the CHANGELOG, runs the full local pipeline, commits,
  creates an annotated tag and (after confirmation) pushes. GitHub Actions then re-validates everything and
  publishes the installer.

.EXAMPLE
  pwsh tools/release.ps1 -Version 2.1.0            # interactive: asks before pushing
  pwsh tools/release.ps1 -Version 2.1.0 -DryRun    # everything except commit/tag/push; changes are reverted
  pwsh tools/release.ps1 -Version 2.1.0-beta.1     # pre-release (published as such on GitHub)
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [switch]$DryRun,
    [switch]$Yes,          # don't ask before pushing
    [switch]$SkipPipeline  # NOT recommended: CI will still run every stage before publishing
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$asmFile = 'src/GamesHub/Properties/AssemblyInfo.cs'
$repoUrl = 'https://github.com/brunocsilva41/GamesHub'
$tag = "v$Version"

function Fail([string]$msg) { Write-Host "✖ $msg" -ForegroundColor Red; exit 1 }
function Step([string]$msg) { Write-Host "▸ $msg" -ForegroundColor Cyan }
function Parse([string]$v) {
    $core, $pre = $v -split '-', 2
    [pscustomobject]@{ Core = [version]$core; Pre = $pre }
}
function IsNewer([string]$a, [string]$b) {   # a > b ?
    $x = Parse $a; $y = Parse $b
    if ($x.Core -ne $y.Core) { return $x.Core -gt $y.Core }
    if (-not $x.Pre -and $y.Pre) { return $true }          # 2.1.0 > 2.1.0-beta
    if ($x.Pre -and -not $y.Pre) { return $false }
    return [string]::CompareOrdinal($x.Pre, $y.Pre) -gt 0
}

# ------------------------------------------------------------------ preconditions
Step 'Checking the repository'
if (git status --porcelain) { Fail 'Working tree is not clean. Commit or stash your changes first.' }
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') { Fail "Releases are cut from main (current branch: $branch)." }
git fetch --quiet --tags origin 2>$null
if ($LASTEXITCODE -eq 0) {
    $behind = [int](git rev-list --count HEAD..origin/main)
    if ($behind -gt 0) { Fail "main is $behind commit(s) behind origin/main. Run git pull first." }
} else { Write-Host '  (no remote reachable — skipping up-to-date check)' -ForegroundColor Yellow }
if (git tag --list $tag) { Fail "Tag $tag already exists." }

# The publish job signs the update manifest; without the secret it would fail after the whole pipeline ran.
if (Get-Command gh -ErrorAction SilentlyContinue) {
    $secrets = @(gh secret list --env release --json name --jq '.[].name' 2>$null) + @(gh secret list --json name --jq '.[].name' 2>$null)
    if ($secrets -contains 'GAMESHUB_UPDATE_SIGNING_KEY') { Write-Host '  update signing secret: present' -ForegroundColor Green }
    elseif ($secrets.Count -gt 0 -and -not $DryRun) { Fail 'Secret GAMESHUB_UPDATE_SIGNING_KEY is missing (see docs/PIPELINE.md, "Assinatura das atualizações").' }
    else { Write-Host '  (could not confirm the GAMESHUB_UPDATE_SIGNING_KEY secret; the release workflow will fail without it)' -ForegroundColor Yellow }
}

$asm = Get-Content $asmFile -Raw
$current = [regex]::Match($asm, 'AssemblyInformationalVersion\("([^"]+)"\)').Groups[1].Value
if (-not (IsNewer $Version $current)) { Fail "Version $Version must be greater than the current $current." }

$changelog = Get-Content 'CHANGELOG.md' -Raw
$m = [regex]::Match($changelog, '(?ms)^## \[Não lançado\]\s*$(.*?)(?=^## \[)')
if (-not $m.Success) { Fail 'CHANGELOG.md has no "## [Não lançado]" section.' }
$unreleased = $m.Groups[1].Value.Trim()
if ($unreleased.Length -lt 10) { Fail 'The "## [Não lançado]" section of CHANGELOG.md is empty — describe the changes first.' }
Write-Host "  $current → $Version" -ForegroundColor Green

# ------------------------------------------------------------------ bump
Step "Updating $asmFile and CHANGELOG.md"
$core4 = ($Version -replace '-.*$', '') + '.0'
$asm = $asm -replace 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(""$core4"")" `
            -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(""$core4"")" `
            -replace 'AssemblyInformationalVersion\("[^"]+"\)', "AssemblyInformationalVersion(""$Version"")"
$date = Get-Date -Format 'yyyy-MM-dd'
$nl = if ($changelog.Contains("`r`n")) { "`r`n" } else { "`n" }
$changelog = $changelog.Remove($m.Index, $m.Length).Insert($m.Index, "## [Não lançado]$nl$nl## [$Version] - $date$nl$nl$unreleased$nl$nl")
# link references at the bottom
$prevTag = "v$current"
$changelog = [regex]::Replace($changelog, '(?m)^\[Não lançado\]:.*$', "[Não lançado]: $repoUrl/compare/$tag...HEAD")
$link = if (git tag --list $prevTag) { "[$Version]: $repoUrl/compare/$prevTag...$tag" } else { "[$Version]: $repoUrl/releases/tag/$tag" }
if ($changelog -match '(?m)^\[Não lançado\]:.*$') { $changelog = [regex]::Replace($changelog, '(?m)^(\[Não lançado\]:.*)$', "`$1$nl$link", 1) }
else { $changelog = $changelog.TrimEnd() + "$nl$nl[Não lançado]: $repoUrl/compare/$tag...HEAD$nl$link$nl" }
Set-Content $asmFile $asm -NoNewline -Encoding utf8NoBOM
Set-Content 'CHANGELOG.md' $changelog -NoNewline -Encoding utf8NoBOM

function Revert { git checkout -- $asmFile CHANGELOG.md; Write-Host '  changes reverted' -ForegroundColor Yellow }

# ------------------------------------------------------------------ pipeline
if (-not $SkipPipeline) {
    Step 'Running the full local pipeline (same stages as CI; E2E in isolated app mode)'
    pwsh -NoProfile -File ci/Invoke-Pipeline.ps1
    if ($LASTEXITCODE -ne 0) { Revert; Fail 'Pipeline failed — nothing was committed.' }
} else { Write-Host '  pipeline skipped locally (CI will still run it)' -ForegroundColor Yellow }

if ($DryRun) { Revert; Write-Host "✔ Dry run OK: $tag would be released." -ForegroundColor Green; exit 0 }

# ------------------------------------------------------------------ commit + tag
Step "Committing and tagging $tag"
git add $asmFile CHANGELOG.md
git commit --quiet -m "Release $tag"
if ($LASTEXITCODE -ne 0) { Revert; Fail 'git commit failed.' }
$notes = "GamesHub $Version`n`n$unreleased"
git tag -a $tag -m $notes
if ($LASTEXITCODE -ne 0) { Fail 'git tag failed (the release commit exists; delete it with git reset --hard HEAD~1 if needed).' }

if (-not $Yes) {
    $answer = Read-Host "Push main and $tag to origin now? GitHub Actions will validate and publish the installer. [y/N]"
    if ($answer -notmatch '^(y|s|yes|sim)$') {
        Write-Host "Not pushed. When ready: git push origin main; git push origin $tag" -ForegroundColor Yellow
        exit 0
    }
}
Step 'Pushing'
git push origin main
if ($LASTEXITCODE -ne 0) { Fail 'Push of main failed.' }
git push origin $tag
if ($LASTEXITCODE -ne 0) { Fail "Push of $tag failed." }
Write-Host "✔ $tag pushed. Follow the release: $repoUrl/actions/workflows/release.yml" -ForegroundColor Green
