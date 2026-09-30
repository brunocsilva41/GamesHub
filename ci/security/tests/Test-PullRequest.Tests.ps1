#Requires -Version 7
<#
.SYNOPSIS
  Self-test of the PR security gate (ci/security/Test-PullRequest.ps1) and the shared file policy. Builds a throwaway
  git repository with synthetic commits (git plumbing, so file names Windows cannot create on disk work too), runs the
  gate against each one and checks its findings and verdict. Needs only git + PowerShell 7 (no Pester).
  Run by the Hygiene stage; also runnable alone: pwsh ci/security/tests/Test-PullRequest.Tests.ps1
#>
param()
. (Join-Path $PSScriptRoot '..' '..' 'lib' 'Common.ps1')
. (Join-Path $PSScriptRoot '..' 'FilePolicy.ps1')
$gate = (Resolve-Path (Join-Path $PSScriptRoot '..' 'Test-PullRequest.ps1')).Path
$report = Join-Path (Get-RepoRoot) 'dist' 'reports' 'stage-pr-security-gate.json'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("gh-gate-test-" + [guid]::NewGuid().ToString('N').Substring(0, 12))
$env:GIT_AUTHOR_NAME = $env:GIT_COMMITTER_NAME = 'gate-test'
$env:GIT_AUTHOR_EMAIL = $env:GIT_COMMITTER_EMAIL = 'gate-test@example.invalid'

# protectNTFS off: the index may hold names (quotes, …) a Linux contributor can commit but NTFS cannot store.
function Invoke-TestGit { git -C $tmp -c core.quotePath=false -c core.protectNTFS=false @args; if ($LASTEXITCODE) { throw "git $args failed ($LASTEXITCODE)" } }

# Commit = parent + files (path -> text content), built in a private index so nothing touches the disk tree.
function New-Commit([string]$Parent, [hashtable]$Files) {
    $env:GIT_INDEX_FILE = Join-Path $tmp '.git' 'test-index'
    try {
        if ($Parent) { Invoke-TestGit read-tree $Parent } else { Invoke-TestGit read-tree --empty }
        foreach ($path in $Files.Keys) {
            $blobFile = Join-Path $tmp '.git' 'blob.tmp'
            [IO.File]::WriteAllText($blobFile, $Files[$path], [Text.UTF8Encoding]::new($false))
            $sha = "$(Invoke-TestGit hash-object -w --no-filters -- $blobFile)".Trim()
            Invoke-TestGit update-index --add --cacheinfo "100644,$sha,$path"
        }
        $tree = "$(Invoke-TestGit write-tree)".Trim()
        $argv = @('commit-tree', $tree, '-m', 'test') + $(if ($Parent) { @('-p', $Parent) } else { @() })
        return "$(Invoke-TestGit @argv)".Trim()
    }
    finally { Remove-Item Env:GIT_INDEX_FILE -ErrorAction SilentlyContinue }
}

# Runs the gate in its own process; returns exit code + recorded checks (from the stage JSON report).
function Invoke-Gate([string]$Head, [string]$Author = 'NONE', [string]$Labels = '', [string]$Action = 'synchronize', [string]$AddedLabel = '') {
    Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
    $out = & pwsh -NoProfile -File $gate -Base $script:base -Head $Head -RepoPath $tmp -Author $Author -Labels $Labels -Action $Action -AddedLabel $AddedLabel 2>&1
    $code = $LASTEXITCODE
    $checks = if (Test-Path -LiteralPath $report) { @(Get-Content -LiteralPath $report -Raw -Encoding utf8 | ConvertFrom-Json) } else { @() }
    if (-not $checks) { $out | Out-Host }
    [pscustomobject]@{ Code = $code; Checks = $checks }
}
function Test-Finding($run, [string]$Path, [string]$NameLike, [int]$Line = -1) {
    [bool]@($run.Checks | Where-Object { $_.File -ceq $Path -and $_.Name -like $NameLike -and ($Line -lt 0 -or $_.Line -eq $Line) }).Count
}

Start-Stage 'PR gate self-test'
try {
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Invoke-TestGit init -q
    $script:base = New-Commit '' @{ 'README.md' = "# test`n"; 'src/App.cs' = "class A {}`n" }

    # (a) non-ASCII / quote characters in the name: git would print them quoted with octal escapes
    $odd = 'src/Ação "x".cs'
    $r = Invoke-Gate (New-Commit $base @{ $odd = "class B {}`nvar p = Process.Start(cmd);`n" })
    Add-Check 'special-character file name: finding on the real path and line' (Test-Finding $r $odd 'risky code: starts processes*' 2)
    Add-Check 'special-character file name: external PR blocked' ($r.Code -ne 0)

    # (b) an added line imitating a file header must not switch the scanned file to an excluded one
    $r = Invoke-Gate (New-Commit $base @{ 'src/Fake.cs' = "++ b/docs/notes.md`nProcess.Start(x);`n" })
    Add-Check 'forged "+++ b/" header inside added content is not a file header' (Test-Finding $r 'src/Fake.cs' 'risky code: starts processes*' 2)

    # (c) added lines that start with "++" are still added content
    $r = Invoke-Gate (New-Commit $base @{ 'web/app.js' = "let i = 0;`n++i; eval(code);`n" })
    Add-Check 'added line starting with "++" is scanned' (Test-Finding $r 'web/app.js' 'risky code: runs dynamically*' 2)

    # (d) tests run in CI, so they are scanned; documentation is not executed and stays excluded
    $r = Invoke-Gate (New-Commit $base @{ 'tests/Evil.cs' = "Process.Start(x);`n"; 'docs/how.md' = "Process.Start(x);`n" })
    Add-Check 'tests/ are scanned' (Test-Finding $r 'tests/Evil.cs' 'risky code: starts processes*' 1)
    Add-Check 'docs/*.md is not scanned' (-not (Test-Finding $r 'docs/how.md' 'risky code*'))

    # (e) executables / scripts / binaries inside web/
    $r = Invoke-Gate (New-Commit $base @{ 'web/run.bat' = "echo`n"; 'web/fonts/x.dll' = "MZ`n"; 'web/tool.ps1' = "Write-Host`n"; 'web/x.vbs' = "x`n" })
    foreach ($p in 'web/run.bat', 'web/fonts/x.dll', 'web/tool.ps1', 'web/x.vbs') {
        Add-Check "forbidden file type flagged: $p" (Test-Finding $r $p 'binary / executable file*')
    }

    # .gitattributes trick: a text file marked "-diff" would hide its added lines
    # (git reads attributes from the checked-out tree, which in CI is the PR's own merge commit)
    [IO.File]::WriteAllText((Join-Path $tmp '.gitattributes'), "*.cs -diff`n")
    $r = Invoke-Gate (New-Commit $base @{ 'src/Hidden.cs' = "Process.Start(x);`n" })
    Remove-Item -LiteralPath (Join-Path $tmp '.gitattributes')
    Add-Check 'text file diffed as binary is flagged' (Test-Finding $r 'src/Hidden.cs' 'diff parse*')

    # clean change from a contributor passes
    $r = Invoke-Gate (New-Commit $base @{ 'src/App.cs' = "class A { int x; }`n" })
    Add-Check 'clean contributor change passes' ($r.Code -eq 0 -and -not @($r.Checks | Where-Object Level -ne 'ok').Count)

    # approval is bound to the commit where the label was added
    $risky = New-Commit $base @{ 'src/Run.cs' = "Process.Start(x);`n" }
    Add-Check 'contributor finding without label blocks' ((Invoke-Gate $risky).Code -ne 0)
    Add-Check 'label added on this commit approves' ((Invoke-Gate $risky -Labels 'bug,seguranca-aprovada' -Action labeled -AddedLabel 'seguranca-aprovada').Code -eq 0)
    Add-Check 'label kept after new commits does not approve' ((Invoke-Gate $risky -Labels 'seguranca-aprovada' -Action synchronize).Code -ne 0)
    Add-Check 'label kept after reopen does not approve' ((Invoke-Gate $risky -Labels 'seguranca-aprovada' -Action reopened).Code -ne 0)
    Add-Check 'another label added does not approve' ((Invoke-Gate $risky -Labels 'seguranca-aprovada,bug' -Action labeled -AddedLabel 'bug').Code -ne 0)
    Add-Check 'maintainer: report only' ((Invoke-Gate $risky -Author 'OWNER').Code -eq 0)

    # shared file policy (also used by the Hygiene stage)
    foreach ($ok in 'web/index.html', 'web/quick/app.mjs', 'web/logo.png', 'web/fonts/outfit-var.woff2', 'web/quick/README.md', 'src/App.cs') {
        Add-Check "file policy allows $ok" (-not (Get-FilePolicyViolation $ok))
    }
    foreach ($bad in 'web/fonts/x.dll', 'web/a.cmd', 'web/sub/logo.png', 'web/fonts/x.exe', 'web/a.ps1', 'tools/x.exe', 'assets/x.lnk') {
        Add-Check "file policy rejects $bad" ([bool](Get-FilePolicyViolation $bad))
    }
}
catch { Add-Check 'self-test ran' $false "$_" }
finally {
    Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
exit (Complete-Stage)
