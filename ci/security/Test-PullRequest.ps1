#Requires -Version 7
<#
.SYNOPSIS
  Pull-request security gate. Reviews the diff between the PR base and head for changes that could smuggle
  malicious behaviour into a Windows app that people install: sensitive paths (workflows, pipeline, build,
  installer, updater, bundled binaries) and risky code patterns in added lines (process launching, download-and-
  run, dynamic code, registry writes, obfuscated blobs, new network hosts, weakened security settings).

  Maintainer PRs (OWNER/MEMBER/COLLABORATOR) get the report only. For anyone else the gate FAILS on findings
  until the maintainer reviews the change and applies the label "seguranca-aprovada" (only people with write
  access can label), which re-runs this check.
.EXAMPLE
  pwsh ci/security/Test-PullRequest.ps1 -Base origin/main -Head HEAD -Author CONTRIBUTOR
#>
param(
    [Parameter(Mandatory)][string]$Base,
    [Parameter(Mandatory)][string]$Head,
    [string]$Author = 'NONE',          # github.event.pull_request.author_association
    [string]$Labels = ''               # comma-separated PR labels
)
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
Start-Stage 'PR security gate'
$root = Get-RepoRoot
Push-Location $root
try {
    $trusted = $Author -in 'OWNER', 'MEMBER', 'COLLABORATOR'
    $approved = ($Labels -split ',' | ForEach-Object Trim) -contains 'seguranca-aprovada'
    $mergeBase = (git merge-base $Base $Head).Trim()
    $changed = @(git diff --name-status --no-renames "$mergeBase..$Head" | ForEach-Object {
        $parts = $_ -split "`t"; [pscustomobject]@{ Status = $parts[0]; Path = $parts[-1] }
    })
    Add-Check 'diff computed' ($changed.Count -ge 0) "$($changed.Count) file(s) changed by $Author"

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
        @{ Rx = '^\.gitattributes$|^\.gitignore$|^CODEOWNERS$|^\.github/CODEOWNERS$'; Why = 'repository policy files' }
    )
    foreach ($c in $changed) {
        foreach ($s in $sensitive) { if ($c.Path -match $s.Rx) { Flag 'sensitive path' $c.Path 0 $s.Why; break } }
        if ($c.Status -ne 'D' -and $c.Path -match '\.(exe|dll|msi|zip|7z|rar|bin|ps1xml|scr|bat|cmd|vbs|js\.map)$' -and $c.Path -notmatch '^web/') {
            Flag 'binary / executable file' $c.Path 0 'executables, archives and scripts must never arrive through a PR'
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
    $current = $null; $lineNo = 0
    foreach ($l in git diff -U0 --no-color "$mergeBase..$Head") {
        if ($l -match '^\+\+\+ b/(.+)$') { $current = $Matches[1]; continue }
        if ($l -match '^@@ -\d+(,\d+)? \+(\d+)') { $lineNo = [int]$Matches[2]; continue }
        if (-not $current -or $l -notmatch '^\+(?!\+\+)') { continue }
        $text = $l.Substring(1)
        if ($current -match '^(tests/|docs/|.*\.md$)') { $lineNo++; continue }   # docs & tests: reviewed, not executed by users
        foreach ($p in $patterns) {
            if ($text -match $p.Rx) { Flag 'risky code' $current $lineNo "$($p.Why): $($Matches[0].Substring(0, [Math]::Min(70, $Matches[0].Length)))" }
        }
        $lineNo++
    }

    # ---- report
    $block = -not $trusted -and -not $approved -and $findings.Count -gt 0
    foreach ($f in $findings) {
        Add-Check "$($f.Kind): $($f.Why)" (-not $block) '' -File $f.Path -Line $f.Line -Warning:(-not $block)
    }
    $verdict = if ($findings.Count -eq 0) { 'no sensitive changes found' }
               elseif ($trusted) { "$($findings.Count) finding(s) — author is a maintainer ($Author), report only" }
               elseif ($approved) { "$($findings.Count) finding(s) — reviewed and approved by a maintainer (label seguranca-aprovada)" }
               else { "$($findings.Count) finding(s) from an external contributor — a maintainer must review every item and add the label 'seguranca-aprovada'" }
    Add-Check 'security gate' (-not $block) $verdict
}
finally { Pop-Location }
exit (Complete-Stage)
