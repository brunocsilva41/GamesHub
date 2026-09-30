#Requires -Version 7
<#
.SYNOPSIS Release gate: the pushed tag must be exactly "v" + the version in AssemblyInfo, point at a commit that is
  on main, have a dated CHANGELOG section with content, and not be published yet.
#>
param([Parameter(Mandatory)][string]$Tag)
# Validate the format before the tag reaches git/gh (it comes from the pushed ref name).
if ($Tag -cnotmatch '^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { Write-Error "Invalid release tag format (expected vMAJOR.MINOR.PATCH[-pre])"; exit 1 }
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
Start-Stage 'Release gate'
$root = Get-RepoRoot
Push-Location $root
try {
    $version = Get-AppVersion
    Add-Check 'tag format vMAJOR.MINOR.PATCH[-pre]' ($Tag -match '^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') $Tag
    Add-Check 'tag matches AssemblyInfo version' ($Tag -eq "v$version") "tag $Tag, AssemblyInfo $version"

    $changelog = Get-Content 'CHANGELOG.md' -Raw
    $esc = [regex]::Escape($version)
    $m = [regex]::Match($changelog, "(?ms)^## \[$esc\] - (\d{4}-\d{2}-\d{2})\s*`$(.*?)(?=^## \[|\z)")
    Add-Check "CHANGELOG section for $version" $m.Success
    if ($m.Success) {
        $body = ($m.Groups[2].Value -replace '(?m)^\[[^\]]+\]:.*$', '').Trim()
        Add-Check 'CHANGELOG section has content' ($body.Length -ge 20) "$($body.Length) chars"
        $date = [datetime]::ParseExact($m.Groups[1].Value, 'yyyy-MM-dd', $null)
        Add-Check 'release date is not in the future' ($date -le (Get-Date).Date.AddDays(1)) $m.Groups[1].Value
    }

    git fetch --quiet origin main 2>$null
    $tagCommit = (git rev-list -n 1 $Tag 2>$null)
    Add-Check 'tag resolves to a commit' ([bool]$tagCommit) $tagCommit
    if ($tagCommit) {
        git merge-base --is-ancestor $tagCommit origin/main 2>$null
        Add-Check 'tagged commit is on main' ($LASTEXITCODE -eq 0) 'releases are cut from main only'
    }
    $tagType = (git cat-file -t $Tag 2>$null)
    Add-Check 'annotated tag' ($tagType -eq 'tag') "type: $tagType"

    if ($env:GITHUB_TOKEN -or $env:GH_TOKEN) {
        gh release view $Tag *> $null
        Add-Check 'release not published yet' ($LASTEXITCODE -ne 0) $Tag
    }
}
finally { Pop-Location }
exit (Complete-Stage)
