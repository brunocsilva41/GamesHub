#Requires -Version 7
<#
.SYNOPSIS Stage 1 — repository hygiene. Runs on any OS (only needs git + PowerShell 7).
  Secrets and personal data, file encodings and line endings, binary allowlist with pinned hashes,
  required files, JSON validity, Markdown links, version consistency.
#>
param()
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
Start-Stage 'Hygiene'
$root = Get-RepoRoot
$files = Get-TrackedFiles

# ------------------------------------------------------------------ required files
$required = 'LICENSE', 'README.md', 'CHANGELOG.md', 'SECURITY.md', 'CONTRIBUTING.md', '.gitattributes', '.gitignore',
            'docs/ARCHITECTURE.md', 'docs/PIPELINE.md', 'lib/checksums.sha256', 'build.ps1',
            'src/GamesHub/Properties/AssemblyInfo.cs', '.github/workflows/ci.yml', '.github/workflows/release.yml',
            '.github/workflows/codeql.yml', '.github/workflows/pr-guard.yml', '.github/CODEOWNERS',
            '.github/codeql/codeql-config.yml', 'ci/security/Test-PullRequest.ps1', 'ci/security/FilePolicy.ps1',
            'ci/security/tests/Test-PullRequest.Tests.ps1'
foreach ($r in $required) { Add-Check "required file: $r" ($files -contains $r) }

# ------------------------------------------------------------------ binaries
. (Join-Path $root 'ci' 'security' 'FilePolicy.ps1')
$binaryExt = '.dll', '.exe', '.ico', '.png', '.jpg', '.jpeg', '.gif', '.woff2', '.zip', '.pdb'
# web/ ships inside the app: only the bundled font and the top-level icon/logo (see ci/security/FilePolicy.ps1).
$allowedBinaryDirs = '^(lib/|assets/|web/(fonts/[^/]+\.woff2|[^/]+\.(png|ico))$|docs/screenshots/)'
$maxBytes = 3MB
foreach ($f in $files) {
    $full = Join-Path $root $f
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }   # deleted, or a nested repository/worktree
    $ext = [IO.Path]::GetExtension($f).ToLowerInvariant()
    $size = (Get-Item -LiteralPath $full -Force).Length
    if ($size -gt $maxBytes) { Add-Check 'file size ≤ 3 MB' $false "$([math]::Round($size / 1MB, 1)) MB" -File $f }
    if ($binaryExt -contains $ext) {
        if ($ext -in '.exe', '.pdb', '.zip') { Add-Check 'no build output committed' $false 'executables/archives never belong in git' -File $f }
        elseif ($f -notmatch $allowedBinaryDirs) { Add-Check 'binary in an allowed folder' $false 'binaries only in lib/, assets/, web/fonts/*.woff2, web/*.png|ico, docs/screenshots' -File $f }
    }
    if ($f -match '^web/') {
        $why = Get-FilePolicyViolation $f   # scripts (.bat/.cmd/.vbs/.ps1…) and anything else unexpected in the UI folder
        if ($why) { Add-Check 'allowed file type in web/' $false $why -File $f }
    }
}
Add-Check 'binary files in allowed folders / sizes' $true "$($files.Count) tracked files scanned"

# Pinned third-party binaries: lib/*.dll must match lib/checksums.sha256 exactly (and vice versa).
$pins = @{}
foreach ($line in Get-Content (Join-Path $root 'lib/checksums.sha256')) {
    if ($line -match '^([0-9a-f]{64}) \*?(.+)$') { $pins[$Matches[2].Trim()] = $Matches[1] }
}
$libDlls = @($files | Where-Object { $_ -match '^lib/[^/]+\.dll$' })
foreach ($dll in $libDlls) {
    $name = Split-Path $dll -Leaf
    $actual = (Get-FileHash (Join-Path $root $dll) -Algorithm SHA256).Hash.ToLowerInvariant()
    Add-Check "pinned hash: $name" ($pins.ContainsKey($name) -and $pins[$name] -eq $actual) $(if ($pins.ContainsKey($name)) { '' } else { 'not listed in lib/checksums.sha256' }) -File $dll
}
foreach ($p in $pins.Keys) { if ($libDlls -notcontains "lib/$p") { Add-Check "pinned file exists: $p" $false 'listed but not tracked' -File 'lib/checksums.sha256' } }

# ------------------------------------------------------------------ text content
$textExt = '.cs', '.js', '.mjs', '.css', '.html', '.md', '.json', '.yml', '.yaml', '.ps1', '.psm1', '.txt', '.manifest', '.config', '.sha256', ''
$utf8 = [Text.UTF8Encoding]::new($false, $true)   # throws on invalid bytes

# Patterns that must never be committed. Each: name, regex, allowlist regex for the matched text.
$forbidden = @(
    @{ Name = 'private key';            Rx = '-----BEGIN (RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY' },
    @{ Name = 'GitHub token';           Rx = '\b(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})' },
    @{ Name = 'AWS access key';         Rx = '\bAKIA[0-9A-Z]{16}\b' },
    @{ Name = 'generic secret assignment'; Rx = '(?i)\b(api[_-]?key|secret|token|password|passwd)\b\s*[:=]\s*["''][A-Za-z0-9_\-]{24,}["'']' },
    @{ Name = 'bearer credential';      Rx = '(?i)bearer\s+[a-f0-9]{32}\b' },
    # Fixture profile names used by tests/mocks are fine; anything else looks like a real profile.
    @{ Name = 'personal Windows path';  Rx = '(?i)\b[A-Z]:\\+Users\\+(?!(Public|Default|User|Users|Usuario|Usuário|Test|Tester|Example|Me|Someone|Alice|Bob|Demo|Jogador|Player|x|y|u|\.\.|<[^>]+>|%[^%]+%|\$|\{)(\\|"|''|$))[^\\\s"''`<>]+' },
    @{ Name = 'personal email';         Rx = '(?i)\b[A-Z0-9._%+-]+@(gmail|hotmail|outlook|yahoo|bilhon|live|icloud)\.[a-z.]+\b' },
    @{ Name = 'merge conflict marker';  Rx = '^(<<<<<<<|>>>>>>>) ' },
    @{ Name = 'development leftover';   Rx = '(?i)(^|\s)(//|#|/\*)\s*OWNER:|^Owner:|Co-Authored-By:|\bwave[- ]?2\b|\b(CORE|LIB|ART|WEB|DIST|QUICK) agent\b' }
)
$hits = 0
foreach ($f in $files) {
    $ext = [IO.Path]::GetExtension($f).ToLowerInvariant()
    if ($textExt -notcontains $ext -or $f -like 'lib/*') { continue }
    $full = Join-Path $root $f
    if (-not (Test-Path $full)) { continue }
    $bytes = [IO.File]::ReadAllBytes($full)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    try { $text = $utf8.GetString($bytes) } catch { Add-Check 'valid UTF-8' $false 'invalid byte sequence' -File $f; continue }

    # Windows PowerShell 5.1 reads BOM-less scripts as ANSI: non-ASCII scripts need a BOM.
    if ($ext -in '.ps1', '.psm1' -and -not $hasBom -and $text -match '[^\x00-\x7F]') {
        Add-Check 'PowerShell script encoding' $false 'contains non-ASCII characters but has no UTF-8 BOM' -File $f
    }
    if ($ext -in '.cs', '.js', '.mjs', '.css', '.html', '.json', '.yml', '.md' -and $hasBom) {
        Add-Check 'no BOM in source files' $false 'UTF-8 BOM found' -File $f -Warning
    }
    if ($text.Contains("`r`n") -and $ext -notin '.ps1', '.psm1', '.cmd', '.bat', '.manifest') {
        Add-Check 'LF line endings' $false 'CRLF found (see .gitattributes; run git add --renormalize .)' -File $f
    }
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($p in $forbidden) {
            if ($f -like 'ci/stages/Hygiene.ps1') { break }   # this file defines the patterns
            if ($lines[$i] -match $p.Rx) {
                $hits++
                Add-Check "no $($p.Name)" $false ($Matches[0].Substring(0, [Math]::Min(60, $Matches[0].Length))) -File $f -Line ($i + 1)
            }
        }
    }
}
Add-Check 'secrets / personal data / leftovers scan' ($hits -eq 0) "$hits finding(s)"

# ------------------------------------------------------------------ JSON validity
foreach ($f in $files | Where-Object { $_ -match '\.json$' }) {
    try { Get-Content (Join-Path $root $f) -Raw | ConvertFrom-Json -Depth 64 | Out-Null; $ok = $true } catch { $ok = $false }
    Add-Check 'valid JSON' $ok -File $f
}

# ------------------------------------------------------------------ Markdown relative links
$mdBroken = 0
foreach ($f in $files | Where-Object { $_ -match '\.md$' -and $_ -notmatch '^\.github/' }) {
    $dir = Split-Path (Join-Path $root $f) -Parent
    $n = 0
    foreach ($line in Get-Content (Join-Path $root $f)) {
        $n++
        foreach ($m in [regex]::Matches($line, '\]\(([^)\s#]+)(#[^)]*)?\)')) {
            $target = $m.Groups[1].Value
            if ($target -match '^(https?:|mailto:)') { continue }
            if (-not (Test-Path (Join-Path $dir ([uri]::UnescapeDataString($target))))) {
                $mdBroken++
                Add-Check 'Markdown link target exists' $false $target -File $f -Line $n
            }
        }
    }
}
Add-Check 'Markdown relative links' ($mdBroken -eq 0) "$mdBroken broken"

# ------------------------------------------------------------------ version consistency
$info = Get-Content (Join-Path $root 'src/GamesHub/Properties/AssemblyInfo.cs') -Raw
$ver = Get-AppVersion
$asm = [regex]::Match($info, 'AssemblyVersion\("([^"]+)"\)').Groups[1].Value
$file = [regex]::Match($info, 'AssemblyFileVersion\("([^"]+)"\)').Groups[1].Value
Add-Check 'version is SemVer' ($ver -match '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') $ver
$core = ($ver -replace '-.*$', '') + '.0'
Add-Check 'AssemblyVersion matches' ($asm -eq $core) "AssemblyVersion=$asm, expected $core" -File 'src/GamesHub/Properties/AssemblyInfo.cs'
Add-Check 'AssemblyFileVersion matches' ($file -eq $core) "AssemblyFileVersion=$file, expected $core" -File 'src/GamesHub/Properties/AssemblyInfo.cs'
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
Add-Check 'CHANGELOG has "## [Não lançado]"' ($changelog -match '(?m)^## \[Não lançado\]') -File 'CHANGELOG.md'
if ($ver -notmatch '-') {
    $esc = [regex]::Escape($ver)
    Add-Check "CHANGELOG has a dated section for $ver" ($changelog -match "(?m)^## \[$esc\] - \d{4}-\d{2}-\d{2}\s*$") -File 'CHANGELOG.md'
}

# ------------------------------------------------------------------ PR security gate self-test
# Synthetic diffs for every known bypass of ci/security/Test-PullRequest.ps1 (quoted names, forged headers, "++"
# lines, tests/, web/ file types, binary-diff attributes, approval bound to a commit).
Invoke-Checked 'PR security gate self-test (ci/security/tests)' {
    pwsh -NoProfile -File (Join-Path $root 'ci' 'security' 'tests' 'Test-PullRequest.Tests.ps1')
} | Out-Null

exit (Complete-Stage)
