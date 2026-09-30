#Requires -Version 7
<#
.SYNOPSIS
  Pull-request security gate. Reviews the diff between the PR base and head for changes that could smuggle
  malicious behaviour into a Windows app that people install: sensitive paths (workflows, pipeline, build,
  installer, updater, bundled binaries), forbidden file types and risky code patterns in added lines (process
  launching, download-and-run, dynamic code, registry writes, obfuscated blobs, new network hosts, weakened
  security settings).

  Maintainer PRs (OWNER/MEMBER/COLLABORATOR) get the report only. For anyone else the gate FAILS on findings
  until the maintainer reviews the change and applies the label "seguranca-aprovada" (only people with write
  access can label). The approval is bound to the commit it was given on: it counts only in the run triggered by
  adding that label (-Action labeled -AddedLabel seguranca-aprovada). Any later run — new commits (synchronize),
  reopen, other label changes — fails again until the maintainer re-reviews and re-applies the label.

  In CI this script runs from a checkout of the BASE commit (the PR cannot change its own gate); the PR checkout is
  only read as git data through -RepoPath.
.EXAMPLE
  pwsh ci/security/Test-PullRequest.ps1 -Base origin/main -Head HEAD -Author CONTRIBUTOR
#>
param(
    [Parameter(Mandatory)][string]$Base,
    [Parameter(Mandatory)][string]$Head,
    [string]$Author = 'NONE',          # github.event.pull_request.author_association
    [string]$Labels = '',              # comma-separated PR labels
    [string]$Action = '',              # github.event.action (opened, synchronize, labeled, ...)
    [string]$AddedLabel = '',          # github.event.label.name (labeled / unlabeled events)
    [string]$RepoPath = ''             # git repository holding the PR commits (default: this repository)
)
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
. (Join-Path $PSScriptRoot 'FilePolicy.ps1')
Start-Stage 'PR security gate'
$repo = if ($RepoPath) { (Resolve-Path -LiteralPath $RepoPath).Path } else { Get-RepoRoot }
$approvalLabel = 'seguranca-aprovada'

# Every git call: this repository, real (unquoted) paths (diff drivers/textconv are disabled per command below).
# git prints paths as UTF-8: decode them as such on every OS (Windows consoles default to an OEM code page).
function Invoke-Git {
    $prev = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    try { git -C $repo -c core.quotePath=false @args }
    finally { [Console]::OutputEncoding = $prev }
}

$trusted = $Author -in 'OWNER', 'MEMBER', 'COLLABORATOR'
$labelled = ($Labels -split ',' | ForEach-Object Trim) -contains $approvalLabel
$approved = $labelled -and $Action -eq 'labeled' -and $AddedLabel -eq $approvalLabel
$mergeBase = "$(Invoke-Git merge-base $Base $Head)".Trim()
Add-Check 'merge base found' ([bool]$mergeBase) "$Base / $Head"
if (-not $mergeBase) { exit (Complete-Stage) }

# -z: NUL-separated "status NUL path NUL" pairs, so names with quotes, tabs, newlines or non-ASCII arrive verbatim.
$raw = (Invoke-Git diff --name-status -z --no-renames --no-ext-diff "$mergeBase..$Head") -join "`n"
$fields = @($raw -split "`0" | Where-Object { $_ -ne '' })
$changed = [System.Collections.Generic.List[object]]::new()
for ($i = 0; $i + 1 -lt $fields.Count; $i += 2) { $changed.Add([pscustomobject]@{ Status = $fields[$i]; Path = $fields[$i + 1] }) }
Add-Check 'diff computed' ($fields.Count % 2 -eq 0) "$($changed.Count) file(s) changed by $Author"

$findings = [System.Collections.Generic.List[object]]::new()
function Flag([string]$kind, [string]$path, [int]$line, [string]$why) {
    $findings.Add([pscustomobject]@{ Kind = $kind; Path = $path; Line = $line; Why = $why })
}

# ---- sensitive paths: code that runs in CI, builds the installer, updates users' machines, or ships binaries
$sensitive = @(
    @{ Rx = '^\.github/'; Why = 'workflows / repository automation (runs in CI with tokens)' },
    @{ Rx = '^ci/|^tools/|^build\.ps1$'; Why = 'pipeline and build scripts' },
    @{ Rx = '^lib/'; Why = 'bundled third-party binaries' },
    @{ Rx = '^src/Installer/|^assets/manifests/'; Why = 'installer / executable manifests' },
    @{ Rx = '^src/GamesHub/Update/'; Why = 'auto-updater (downloads and runs installers)' },
    @{ Rx = '^\.gitattributes$|^\.gitignore$|^CODEOWNERS$|^\.github/CODEOWNERS$|(^|/)\.gitmodules$'; Why = 'repository policy files' }
)
foreach ($c in $changed) {
    foreach ($s in $sensitive) { if ($c.Path -match $s.Rx) { Flag 'sensitive path' $c.Path 0 $s.Why; break } }
    if ($c.Status -ne 'D') {
        $why = Get-FilePolicyViolation $c.Path
        if ($why) { Flag 'binary / executable file' $c.Path 0 $why }
    }
}

# ---- risky patterns in ADDED lines
$patterns = @(
    @{ Rx = '\bProcess\.Start\b|ShellExecute|CreateProcess|\bcmd(\.exe)?\s+/c\b|Start-Process|\bpowershell(\.exe)?\s+-(e|enc|encodedcommand)\b'; Why = 'starts processes' },
    @{ Rx = 'Invoke-Expression|\biex\b|\beval\b|new\s+Function\s*\(|setTimeout\s*\(\s*["''`]|Assembly\.Load|Reflection\.Emit|CSharpCodeProvider|Add-Type\s+-TypeDefinition'; Why = 'runs dynamically generated code' },
    @{ Rx = 'DownloadFile|DownloadString|DownloadData|Invoke-WebRequest|Invoke-RestMethod|\bcurl\b|\bwget\b|WebClient|HttpClient|fetch\s*\(\s*["''`]https?:'; Why = 'network download' },
    @{ Rx = 'Registry(Key)?\.(SetValue|CreateSubKey|DeleteSubKey|DeleteValue)|reg(\.exe)?\s+(add|delete)|HKLM:|HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'; Why = 'writes to the Windows registry / autostart' },
    @{ Rx = '[A-Za-z0-9+/]{160,}={0,2}|(\\x[0-9a-fA-F]{2}){24,}|(0x[0-9a-fA-F]{2},\s*){24,}'; Why = 'long encoded blob (possible obfuscated payload)' },
    @{ Rx = 'pull_request_target|workflow_run|secrets\.|permissions:\s*write-all|contents:\s*write|GITHUB_TOKEN|ACTIONS_RUNTIME_TOKEN|id-token:\s*write'; Why = 'workflow privileges / secrets access' },
    @{ Rx = 'Content-Security-Policy|AreDevToolsEnabled|IsWebMessageEnabled|AllowExternalDrop|SetVirtualHostNameToFolderMapping|--remote-debugging|unsafe-eval|NoProxy|ServerCertificateValidationCallback|CertificatePolicy'; Why = 'weakens a security boundary (CSP, WebView2, TLS)' },
    @{ Rx = '\b(File\.Delete|Directory\.Delete|Remove-Item)\b.*(Recurse|true)|DeleteSubKeyTree'; Why = 'recursive deletion' },
    @{ Rx = 'https?://(?!(app|art)\.gameshub\.example|github\.com/brunocsilva41|(www\.)?(steamgriddb|pcgamingwiki)\.com|[a-z0-9.-]*steam(static|powered|community)\.com|keepachangelog\.com|semver\.org)[a-z0-9.-]+\.[a-z]{2,}'; Why = 'new network host' }
)
# Documentation is reviewed but never executed. Tests are scanned: they run in CI.
$notExecuted = '^docs/|\.md$'
# Files git may legitimately diff as binary.
$binaryExt = '(?i)\.(png|ico|jpe?g|gif|webp|woff2?|ttf|otf|pdf|dll|exe|zip|pdb)$'

# Unified diff parser. File sections start with "diff --git" and are matched, in order, to the -z name list above
# (both come from the same diff, one entry per changed path), so the file name never comes from a text header that
# a crafted path or added line could imitate. Inside a section, header lines ("---", "+++", "index", mode lines)
# only appear before the first "@@"; after it every line starting with "+" is added content — including "+++…".
$fileIndex = -1; $current = $null; $inHunk = $false; $lineNo = 0
foreach ($l in Invoke-Git diff -U0 --no-color --no-ext-diff --no-textconv --no-renames "$mergeBase..$Head") {
    if ($l.StartsWith('diff --git ')) {
        $fileIndex++; $inHunk = $false
        $current = if ($fileIndex -lt $changed.Count) { $changed[$fileIndex].Path } else { $null }
        if (-not $current) { Flag 'diff parse' '' 0 'more file sections in the diff than changed paths — review the raw diff' }
        continue
    }
    if (-not $inHunk) {
        if ($l -match '^@@ -\d+(,\d+)? \+(\d+)') { $lineNo = [int]$Matches[2]; $inHunk = $true }
        elseif ($current -and $l -match '^(Binary files |GIT binary patch)' -and $current -notmatch $binaryExt) {
            # a .gitattributes "-diff"/"binary" rule would otherwise hide the added lines of a text file from this scan
            Flag 'diff parse' $current 0 'text file diffed as binary (check .gitattributes) — its added lines could not be scanned'
        }
        elseif ($current -and $l.StartsWith('+++ ') -and $l -ne '+++ /dev/null' -and $l -ne "+++ b/$current" -and -not $l.StartsWith('+++ "')) {
            Flag 'diff parse' $current 0 "file header '$l' does not match the changed path — review the raw diff"
        }
        continue
    }
    if ($l -match '^@@ -\d+(,\d+)? \+(\d+)') { $lineNo = [int]$Matches[2]; continue }
    if (-not $l.StartsWith('+')) { continue }   # removed lines, "\ No newline at end of file"
    $text = $l.Substring(1)
    if ($current -and $current -notmatch $notExecuted) {
        foreach ($p in $patterns) {
            if ($text -match $p.Rx) { Flag 'risky code' $current $lineNo "$($p.Why): $($Matches[0].Substring(0, [Math]::Min(70, $Matches[0].Length)))" }
        }
    }
    $lineNo++
}
if ($fileIndex + 1 -ne $changed.Count) { Flag 'diff parse' '' 0 "diff has $($fileIndex + 1) file section(s) for $($changed.Count) changed path(s) — review the raw diff" }

# ---- report
$block = -not $trusted -and -not $approved -and $findings.Count -gt 0
foreach ($f in $findings) {
    Add-Check "$($f.Kind): $($f.Why)" (-not $block) '' -File $f.Path -Line $f.Line -Warning:(-not $block)
}
$verdict = if ($findings.Count -eq 0) { 'no sensitive changes found' }
           elseif ($trusted) { "$($findings.Count) finding(s) — author is a maintainer ($Author), report only" }
           elseif ($approved) { "$($findings.Count) finding(s) — reviewed and approved by a maintainer on this commit (label $approvalLabel)" }
           elseif ($labelled) { "$($findings.Count) finding(s) — the label '$approvalLabel' approves only the commit it was added on; after new commits (or any other PR event) a maintainer must review again and remove + re-add the label" }
           else { "$($findings.Count) finding(s) from an external contributor — a maintainer must review every item and add the label '$approvalLabel'" }
Add-Check 'security gate' (-not $block) $verdict
exit (Complete-Stage)
