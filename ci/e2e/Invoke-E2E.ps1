<#
.SYNOPSIS
  GamesHub end-to-end smoke tests (App mode: safe & isolated; Installer mode: CI only).

.EXAMPLE
  # local / CI: run the built app in an isolated data dir and drive its UI over CDP
  powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode App

  # CI only (GitHub Actions): install, upgrade, test the installed app, uninstall, purge
  powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode Installer -Setup dist/package/GamesHub-Setup-2.0.0.exe

  # anywhere: print the installer plan and validate the Setup file without running it
  powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode Installer -Setup dist/package/GamesHub-Setup-2.0.0.exe -DryRun

.NOTES
  App mode never touches the user's install/data: GAMESHUB_DATA_DIR points to a fresh temp folder (which also gives
  the process its own single-instance mutex/pipe), all network/global-hotkey features are off, the UI is observed only
  through the WebView2 DevTools port of the process started here, and only PIDs started here are ever killed.
#>
[CmdletBinding()]
param(
    [ValidateSet('App', 'Installer')]
    [string]$Mode = 'App',
    [string]$AppDir,
    [string]$Setup,
    [string]$OutDir,
    [int]$TimeoutSec = 90,
    [int]$MemoryBudgetMB = 600,      # GamesHub.exe + its WebView2 process tree (working set)
    [int]$HostMemoryBudgetMB = 150,  # GamesHub.exe alone
    [switch]$RequireSignature,
    [switch]$DryRun,
    [switch]$KeepTemp,
    [switch]$NoSnapshot,             # App mode: run straight from -AppDir instead of a private temp copy
    [switch]$IUnderstandThisModifiesThisMachine
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $AppDir) { $AppDir = Join-Path $RepoRoot 'dist\app' }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'dist\e2e' }
$AppDir = [IO.Path]::GetFullPath($AppDir)
$OutDir = [IO.Path]::GetFullPath($OutDir)
$ExpectedGames = 5
$MainUrl = 'https://app.gameshub.example/index.html'
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GamesHub'

$script:Results = New-Object System.Collections.Generic.List[object]
$script:Owned = @{}   # PID -> start time of every process this script started (or that they spawned)
$script:TempDirs = New-Object System.Collections.Generic.List[string]
$script:JumpListBackup = @()
$Clock = [Diagnostics.Stopwatch]::StartNew()

# ====================================================================== result helpers

function Add-Result([string]$Name, [bool]$Pass, [string]$Detail, [long]$Ms) {
    $script:Results.Add([pscustomobject]@{ name = $Name; pass = $Pass; detail = $Detail; ms = $Ms })
    $mark = if ($Pass) { 'PASS' } else { 'FAIL' }
    $color = if ($Pass) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}  ({2} ms)  {3}" -f $mark, $Name, $Ms, $Detail) -ForegroundColor $color
}

<# Runs $Body; its (string) output is the detail. A throw = failure. Returns $true when it passed. #>
function Test-Step([string]$Name, [scriptblock]$Body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $detail = (& $Body | Out-String).Trim()
        Add-Result $Name $true $detail $sw.ElapsedMilliseconds
        return $true
    } catch {
        Add-Result $Name $false ($_.Exception.Message) $sw.ElapsedMilliseconds
        return $false
    }
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutMs, [int]$IntervalMs = 200) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds $IntervalMs
    }
    return [bool](& $Condition)
}

# ====================================================================== generic helpers

function New-TempDir([string]$Label) {
    $base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
    $d = Join-Path $base ("gameshub-e2e-{0}-{1}" -f $Label, [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $d | Out-Null
    $script:TempDirs.Add($d)
    return $d
}

function Remove-DirRetry([string]$Dir) {
    for ($i = 1; $i -le 20 -and (Test-Path -LiteralPath $Dir); $i++) {
        try { Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction Stop }
        catch { Start-Sleep -Milliseconds 500 } # WebView2 helpers may hold files briefly
    }
    if (Test-Path -LiteralPath $Dir) { Write-Warning "Could not remove $Dir" }
}

function Get-FreePort {
    $l = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $l.Start(); $p = $l.LocalEndpoint.Port; $l.Stop()
    return $p
}

<# SHA-256 via .NET (no dependency on the Microsoft.PowerShell.Utility module being loadable). #>
function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $fs = [IO.File]::OpenRead($Path)
    try { return -join ($sha.ComputeHash($fs) | ForEach-Object { $_.ToString('X2') }) } finally { $fs.Dispose(); $sha.Dispose() }
}

<# Relative path -> SHA256 for every file under $Dir. #>
function Get-TreeHashes([string]$Dir, [string[]]$Exclude = @()) {
    $map = @{}
    $root = (Resolve-Path -LiteralPath $Dir).Path.TrimEnd('\')
    foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $rel = $f.FullName.Substring($root.Length + 1)
        if ($Exclude -contains $rel) { continue }
        $map[$rel] = Get-Sha256 $f.FullName
    }
    return $map
}

function Compare-Trees([hashtable]$Expected, [hashtable]$Actual) {
    $problems = @()
    foreach ($k in $Expected.Keys) {
        if (-not $Actual.ContainsKey($k)) { $problems += "missing $k" }
        elseif ($Actual[$k] -ne $Expected[$k]) { $problems += "hash differs $k" }
    }
    foreach ($k in $Actual.Keys) { if (-not $Expected.ContainsKey($k)) { $problems += "extra $k" } }
    return $problems
}

function Get-TreeFingerprint([string]$Dir) {
    # files only (name|size|mtime): directory timestamps change on mere reads by some scanners
    @(Get-ChildItem -LiteralPath $Dir -Recurse -Force -File | Sort-Object FullName |
        ForEach-Object { '{0}|{1}|{2}' -f $_.FullName.Substring($Dir.Length), $_.Length, $_.LastWriteTimeUtc.Ticks })
}

# ====================================================================== fixture

function New-ShellLink([string]$Path, [string]$Target, [string]$Arguments = '') {
    $sh = New-Object -ComObject WScript.Shell
    try {
        $lnk = $sh.CreateShortcut($Path)
        $lnk.TargetPath = $Target
        if ($Arguments) { $lnk.Arguments = $Arguments }
        $lnk.WorkingDirectory = Split-Path $Target -Parent
        $lnk.Save()
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($sh) }
}

<# 5 games: 2 Steam .url, 1 .lnk (notepad), 1 .exe (copy of whoami), 1 broken .lnk. #>
function New-GamesFixture {
    $dir = New-TempDir 'games'
    Set-Content -LiteralPath (Join-Path $dir 'Counter-Strike 2.url') -Encoding ASCII -Value "[InternetShortcut]`r`nURL=steam://rungameid/730`r`n"
    Set-Content -LiteralPath (Join-Path $dir 'Dota 2.url') -Encoding ASCII -Value "[InternetShortcut]`r`nURL=steam://rungameid/570`r`n"
    New-ShellLink (Join-Path $dir 'Notepad Adventure.lnk') (Join-Path $env:WINDIR 'System32\notepad.exe')
    Copy-Item -LiteralPath (Join-Path $env:WINDIR 'System32\whoami.exe') (Join-Path $dir 'Tiny Quest.exe')
    $ghost = Join-Path $dir 'missing\GhostGame.exe'   # the folder never exists -> broken shortcut
    New-ShellLink (Join-Path $dir 'Ghost Game.lnk') $ghost
    return $dir
}

<# Deterministic, offline, no global hotkeys, no registry (startWithWindows=false). #>
function New-DataDir([string]$GamesDir) {
    $data = New-TempDir 'data'
    $settings = [ordered]@{
        gamesDir = $GamesDir
        autoArtwork = $false; fetchMetadata = $false; pcgwEnabled = $false; checkUpdates = $false
        importSteam = $false; importEpic = $false; importRiot = $false; importHydra = $false; importSteamPlaytime = $false
        trackPlaytime = $false; automationEnabled = $false; hotkeyEnabled = $false; quickLaunchEnabled = $false
        startWithWindows = $false; startMinimized = $false; closeToTray = $false; onLaunch = 'none'; reduceMotion = $true
    }
    [IO.File]::WriteAllText((Join-Path $data 'settings.json'), ($settings | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    # nothing to import from a v1 install
    [IO.File]::WriteAllText((Join-Path $data 'legacy-import.done'), (Get-Date).ToString('o'))
    return $data
}

# ====================================================================== jump list guard
# The app writes the taskbar Jump List for AppUserModelID "GamesHub.App" - shared with a real install on this PC.
# An isolated test run would replace the user's list with an empty one, so back it up and restore it afterwards.

function Backup-JumpList {
    $dir = Join-Path $env:APPDATA 'Microsoft\Windows\Recent\CustomDestinations'
    if (-not (Test-Path $dir)) { return }
    $script:JumpListBackup = @(Get-ChildItem $dir -File | Where-Object {
        [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($_.FullName)) -match 'GamesHub\.exe'
    } | ForEach-Object { [pscustomobject]@{ Path = $_.FullName; Bytes = [IO.File]::ReadAllBytes($_.FullName) } })
}

function Restore-JumpList {
    foreach ($b in $script:JumpListBackup) {
        try {
            $cur = if (Test-Path -LiteralPath $b.Path) { [IO.File]::ReadAllBytes($b.Path) } else { $null }
            if ($cur -eq $null -or [Convert]::ToBase64String($cur) -ne [Convert]::ToBase64String($b.Bytes)) {
                [IO.File]::WriteAllBytes($b.Path, $b.Bytes)
                Write-Host "  (restored the user's GamesHub Jump List: $(Split-Path $b.Path -Leaf))" -ForegroundColor DarkGray
            }
        } catch { Write-Warning "Jump List restore failed: $($_.Exception.Message)" }
    }
}

# ====================================================================== process helpers

function Start-GamesHub([string]$Exe, [string]$DataDir, [int]$Port, [string]$Arguments = '') {
    $psi = New-Object Diagnostics.ProcessStartInfo($Exe, $Arguments)
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $Exe -Parent
    # Environment for THIS child only (never set on the current session).
    $psi.EnvironmentVariables['GAMESHUB_DATA_DIR'] = $DataDir
    $psi.EnvironmentVariables['WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS'] = "--remote-debugging-port=$Port --remote-allow-origins=*"
    $p = [Diagnostics.Process]::Start($psi)
    $null = $p.Handle # keep a handle so ExitCode stays readable after exit
    Add-Owned $p.Id $p.StartTime
    return $p
}

function Add-Owned([int]$Id, $StartTime) {
    if ($StartTime -eq $null) { try { $StartTime = (Get-Process -Id $Id -ErrorAction Stop).StartTime } catch { return } }
    $script:Owned[$Id] = [datetime]$StartTime
}

function Get-ProcessTree([int]$RootPid) {
    $all = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name, WorkingSetSize, PrivatePageCount, ExecutablePath)
    $tree = @(); $seen = New-Object System.Collections.Generic.HashSet[int]; $frontier = @($RootPid)
    while ($frontier.Count) {
        $next = @()
        foreach ($p in $all) {
            if (($frontier -contains [int]$p.ParentProcessId) -and $seen.Add([int]$p.ProcessId)) { $tree += $p; $next += [int]$p.ProcessId }
        }
        $frontier = $next
    }
    return ,$tree
}

function Get-ExeProcesses([string]$Exe) {
    @(Get-CimInstance Win32_Process -Filter "Name='GamesHub.exe'" | Where-Object { $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -ieq $Exe) })
}

function Stop-Owned([int[]]$Pids) {
    foreach ($id in $Pids) {
        if (-not $script:Owned.ContainsKey($id)) { continue } # never kill anything we did not start / spawn
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        # PID reuse guard: same PID + same start time (to the second) as when we recorded it
        try { if ([math]::Abs(($p.StartTime - $script:Owned[$id]).TotalSeconds) -gt 1) { continue } } catch { continue }
        Write-Host "  killing owned PID $id ($($p.ProcessName))" -ForegroundColor Yellow
        try { $p.Kill() } catch { Write-Warning "kill $id failed: $($_.Exception.Message)" }
    }
}

function Read-AppLog([string]$DataDir) {
    $f = Join-Path $DataDir 'logs\gameshub.log'
    if (Test-Path -LiteralPath $f) { return [IO.File]::ReadAllText($f) }
    return ''
}

# ====================================================================== App suite

function Invoke-AppSuite([string]$Exe, [string]$GamesDir, [string]$Label) {
    $pre = if ($Label) { "$Label " } else { '' }                       # check-name prefix
    $tag = if ($Label) { ($Label -replace '[^\w]', '') + '-' } else { '' } # artifact file prefix
    $st = @{ children = @() }                                             # state shared with Test-Step bodies
    $data = New-DataDir $GamesDir
    $port = Get-FreePort
    $app = $null
    $appDirOfExe = Split-Path $Exe -Parent
    $before = Get-TreeFingerprint $appDirOfExe
    Write-Host "`n== App suite ($Label): $Exe`n   data=$data port=$port" -ForegroundColor Cyan

    try {
        $existing = @(Get-ExeProcesses $Exe)
        if (-not (Test-Step "${pre}process: no stale instance of this exe" {
            if ($existing.Count) { throw "already running: PID $($existing.ProcessId -join ',') - close it first" } 'none'
        })) { return }

        $app = Start-GamesHub $Exe $data $port
        $ok = Test-Step "${pre}process: starts and stays alive" {
            Start-Sleep -Seconds 3
            if ($app.HasExited) { throw "exited early with code $($app.ExitCode)" }
            "PID $($app.Id)"
        }
        if (-not $ok) { return }

        $ok = Test-Step "${pre}cdp: DevTools endpoint up" {
            $found = Wait-Until {
                try { @(Invoke-RestMethod "http://127.0.0.1:$port/json/list" -TimeoutSec 2 | Where-Object { $_.type -eq 'page' -and $_.url -eq $MainUrl }).Count -eq 1 }
                catch { $false }
            } ($TimeoutSec * 1000) 300
            if (-not $found) { throw "no page $MainUrl on port $port" }
            "port $port"
        }
        if (-not $ok) { return }
        $st.children = Get-ProcessTree $app.Id
        foreach ($c in $st.children) { Add-Owned ([int]$c.ProcessId) $c.CreationDate }

        # ---- second launch forwards to the first one (same isolated scope) and exits
        Test-Step "${pre}single instance: second launch forwards and exits" {
            $second = Start-GamesHub $Exe $data $port
            if (-not $second.WaitForExit(15000)) { throw "second instance still running after 15 s" }
            if ($second.ExitCode -ne 0) { throw "second instance exit code $($second.ExitCode)" }
            if (-not (Wait-Until { (Read-AppLog $data) -match 'Activation from another instance' } 5000)) { throw "no 'Activation from another instance' in the log" }
            $procs = @(Get-ExeProcesses $Exe)
            if ($procs.Count -ne 1) { throw "$($procs.Count) GamesHub processes for this exe" }
            $pages = @(Invoke-RestMethod "http://127.0.0.1:$port/json/list" | Where-Object { $_.type -eq 'page' -and $_.url -eq $MainUrl })
            if ($pages.Count -ne 1) { throw "$($pages.Count) main windows" }
            "exit 0 in $([int]($second.ExitTime - $second.StartTime).TotalMilliseconds) ms, activation logged, 1 process, 1 window"
        } | Out-Null

        # ---- UI over CDP (node)
        $node = (Get-Command node -ErrorAction SilentlyContinue)
        if (-not $node) {
            Add-Result "${pre}cdp: node available" $false 'node.exe not found on PATH (Node >= 22 required)' 0
        } else {
            $cdpLog = Join-Path $OutDir "${tag}cdp-stderr.log"
            $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue' # PS 5.1: stderr of a native exe must not throw
            try {
                $nodeArgs = @((Join-Path $PSScriptRoot 'cdp.mjs'), '--port', $port, '--out', $OutDir, '--expect-games', $ExpectedGames, '--timeout', $TimeoutSec)
                if ($pre) { $nodeArgs += @('--prefix', $pre, '--file-prefix', $tag) } # PS 5.1 drops empty native args
                $json = & $node.Source @nodeArgs 2> $cdpLog
            } finally { $ErrorActionPreference = $eap }
            $line = @($json | Where-Object { $_ -like '{*' }) | Select-Object -Last 1
            if (-not $line) { Add-Result "${pre}cdp: suite output" $false "no JSON from cdp.mjs (see $cdpLog)" 0 }
            else {
                $r = $line | ConvertFrom-Json
                foreach ($c in $r.checks) { Add-Result $c.name ([bool]$c.pass) $c.detail ([long]$c.ms) }
            }
        }

        # ---- memory
        Test-Step "${pre}memory: working set (host <= $HostMemoryBudgetMB MB, total <= $MemoryBudgetMB MB)" {
            $app.Refresh()
            $tree = Get-ProcessTree $app.Id
            foreach ($c in $tree) { Add-Owned ([int]$c.ProcessId) $c.CreationDate }
            $st.children = $tree
            $hostWs = [math]::Round($app.WorkingSet64 / 1MB)
            $hostPriv = [math]::Round($app.PrivateMemorySize64 / 1MB)
            $treeWs = [math]::Round(($tree | Measure-Object WorkingSetSize -Sum).Sum / 1MB)
            $treePriv = [math]::Round(($tree | Measure-Object PrivatePageCount -Sum).Sum / 1MB)
            $total = $hostWs + $treeWs
            $msg = "host WS $hostWs MB (private $hostPriv MB) + WebView2 $($tree.Count) procs WS $treeWs MB (private $treePriv MB) = $total MB"
            if ($total -gt $MemoryBudgetMB -or $hostWs -gt $HostMemoryBudgetMB) { throw "over budget: $msg" }
            $msg
        } | Out-Null

        Test-Step "${pre}process: still alive after UI run" { if ($app.HasExited) { throw "exited with $($app.ExitCode)" } 'alive' } | Out-Null

        # ---- --quit
        Test-Step "${pre}quit: --quit exits within 10 s with code 0" {
            $qsw = [Diagnostics.Stopwatch]::StartNew()
            $q = Start-GamesHub $Exe $data $port '--quit'
            if (-not $app.WaitForExit(10000)) { throw 'app still running 10 s after --quit' }
            $app.WaitForExit() # flush async exit state
            $ms = $qsw.ElapsedMilliseconds
            [void]$q.WaitForExit(5000)
            if ($q.HasExited -and $q.ExitCode -ne 0) { throw "--quit sender exit code $($q.ExitCode)" }
            if ($app.ExitCode -ne 0) { throw "app exit code $($app.ExitCode)" }
            "app exited with 0 in $ms ms"
        } | Out-Null
        Test-Step "${pre}quit: WebView2 child processes gone" {
            $left = { @($st.children | Where-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }) }
            if (-not (Wait-Until { @(& $left).Count -eq 0 } 10000)) { throw "$(@(& $left).Count) child process(es) still running" }
            "$($st.children.Count) children exited"
        } | Out-Null

        # ---- log
        $log = Read-AppLog $data
        Copy-Item -LiteralPath (Join-Path $data 'logs\gameshub.log') (Join-Path $OutDir "${tag}gameshub.log") -ErrorAction SilentlyContinue
        Test-Step "${pre}log: 'Exited cleanly'" { if ($log -notmatch 'Exited cleanly') { throw 'missing' } 'present' } | Out-Null
        Test-Step "${pre}log: no ERROR lines" {
            $errs = @($log -split "`r?`n" | Where-Object { $_ -match '^\S+ \S+ ERROR ' })
            if ($errs.Count) { throw "$($errs.Count): $($errs[0..([math]::Min(2, $errs.Count - 1))] -join ' || ')" }
            "$(@($log -split "`n" | Where-Object { $_ -match ' WARN ' }).Count) WARN, 0 ERROR"
        } | Out-Null
        Test-Step "${pre}isolation: app folder untouched" {
            $diff = @(Compare-Object $before (Get-TreeFingerprint $appDirOfExe) | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" })
            if ($diff.Count) { throw "changed under ${appDirOfExe}: $($diff[0..([math]::Min(4, $diff.Count - 1))] -join '; ')" }
            "$($before.Count) files unchanged"
        } | Out-Null
    } finally {
        if ($app -and -not $app.HasExited) {
            Write-Host '  app still running: sending --quit' -ForegroundColor Yellow
            try { $q = Start-GamesHub $Exe $data $port '--quit'; [void]$q.WaitForExit(5000); [void]$app.WaitForExit(10000) } catch { }
            if (-not $app.HasExited) { Stop-Owned @($app.Id) }
        }
        Stop-Owned @($st.children | ForEach-Object { [int]$_.ProcessId })
    }
}

# ====================================================================== Installer suite (CI only)

function Get-AsmInfoVersions {
    $txt = Get-Content (Join-Path $RepoRoot 'src\GamesHub\Properties\AssemblyInfo.cs') -Raw
    [pscustomobject]@{
        File = [regex]::Match($txt, 'AssemblyFileVersion\("([^"]+)"\)').Groups[1].Value
        Informational = [regex]::Match($txt, 'AssemblyInformationalVersion\("([^"]+)"\)').Groups[1].Value
    }
}

function Test-SetupFile([string]$SetupExe) {
    $v = Get-AsmInfoVersions
    Test-Step 'setup: file version matches AssemblyInfo' {
        $fi = [Diagnostics.FileVersionInfo]::GetVersionInfo($SetupExe)
        if ($fi.FileVersion -ne $v.File) { throw "FileVersion $($fi.FileVersion) != AssemblyInfo $($v.File)" }
        if ($fi.ProductVersion -ne $v.Informational) { throw "ProductVersion $($fi.ProductVersion) != $($v.Informational)" }
        if ($fi.ProductName -ne 'GamesHub') { throw "ProductName '$($fi.ProductName)'" }
        "FileVersion $($fi.FileVersion), ProductVersion $($fi.ProductVersion), '$($fi.FileDescription)'"
    } | Out-Null
    Test-Step 'setup: file name carries the version' {
        if ((Split-Path $SetupExe -Leaf) -notmatch [regex]::Escape($v.Informational)) { throw "$(Split-Path $SetupExe -Leaf) lacks $($v.Informational)" } 'ok'
    } | Out-Null
    Test-Step 'setup: authenticode signature' {
        $sig = Get-AuthenticodeSignature -LiteralPath $SetupExe
        if ($RequireSignature -and $sig.Status -ne 'Valid') { throw "status $($sig.Status)" }
        "status $($sig.Status)$(if ($sig.SignerCertificate) { ' by ' + $sig.SignerCertificate.Subject })$(if (-not $RequireSignature) { ' (not required)' })"
    } | Out-Null
    $shaFile = "$SetupExe.sha256"
    if (Test-Path -LiteralPath $shaFile) {
        Test-Step 'setup: .sha256 matches' {
            $want = ((Get-Content -LiteralPath $shaFile -Raw).Trim() -split '\s+')[0]
            $got = Get-Sha256 $SetupExe
            if ($want -ne $got) { throw "sha256 file $want != $got" } $got.ToLowerInvariant()
        } | Out-Null
    }
    return $v
}

function Invoke-Setup([string]$SetupExe, [string[]]$SetupArgs, [string]$Label) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) 'GamesHub'
    Remove-Item -LiteralPath (Join-Path $tmp 'setup-result.txt') -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $SetupExe -ArgumentList $SetupArgs -PassThru
    $null = $p.Handle # PS 5.1: without a handle ExitCode is empty after exit
    Add-Owned $p.Id $null
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { Stop-Owned @($p.Id); throw "${Label}: Setup timed out" }
    Copy-Item -LiteralPath (Join-Path $tmp 'setup.log') (Join-Path $OutDir 'setup.log') -Force -ErrorAction SilentlyContinue
    $result = Get-Content -LiteralPath (Join-Path $tmp 'setup-result.txt') -Raw -ErrorAction SilentlyContinue
    if ($p.ExitCode -ne 0) { throw "${Label}: exit code $($p.ExitCode), result '$result'" }
    if ($result -notmatch '^OK:(.+)$') { throw "${Label}: result '$result'" }
    return $Matches[1].Trim()
}

function Invoke-Uninstall([string]$Dir, [string[]]$UninstallArgs) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) 'GamesHub'
    Remove-Item -LiteralPath (Join-Path $tmp 'uninstall-result.txt') -ErrorAction SilentlyContinue
    # Uninstall.exe runs in place, then schedules "cmd /c ping -n 3 & del Uninstall.exe & rmdir <dir>" after it exits.
    $p = Start-Process -FilePath (Join-Path $Dir 'Uninstall.exe') -ArgumentList $UninstallArgs -PassThru -WorkingDirectory ([IO.Path]::GetTempPath())
    $null = $p.Handle # PS 5.1: without a handle ExitCode is empty after exit
    Add-Owned $p.Id $null
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { Stop-Owned @($p.Id); throw 'Uninstall timed out' }
    Copy-Item -LiteralPath (Join-Path $tmp 'uninstall.log') (Join-Path $OutDir 'uninstall.log') -Force -ErrorAction SilentlyContinue
    $result = Get-Content -LiteralPath (Join-Path $tmp 'uninstall-result.txt') -Raw -ErrorAction SilentlyContinue
    if ($p.ExitCode -ne 0 -or "$result".Trim() -ne 'OK') { throw "exit $($p.ExitCode), result '$result'" }
    if (-not (Wait-Until { -not (Test-Path -LiteralPath $Dir) } 20000 500)) {
        throw "install dir still present: $((Get-ChildItem -LiteralPath $Dir -Recurse -Force | ForEach-Object Name) -join ', ')"
    }
    "exit 0, OK, folder removed"
}

function Test-Installed([string]$Dir, [string]$GamesDir, [string]$Version, [string]$Label) {
    Test-Step "$Label installed files match dist/app" {
        $expected = Get-TreeHashes $AppDir
        $actual = Get-TreeHashes $Dir @('install-files.txt')
        $diff = @(Compare-Trees $expected $actual)
        if ($diff.Count) { throw "$($diff.Count) difference(s): $($diff[0..([math]::Min(4, $diff.Count - 1))] -join '; ')" }
        $manifest = @(Get-Content -LiteralPath (Join-Path $Dir 'install-files.txt') | Where-Object { $_ })
        if ($manifest.Count -ne $expected.Count + 1) { throw "install-files.txt lists $($manifest.Count) entries, expected $($expected.Count + 1)" }
        "$($expected.Count) files identical"
    } | Out-Null
    Test-Step "$Label uninstall key" {
        $k = Get-ItemProperty -LiteralPath $UninstallKey
        if ($k.DisplayVersion -ne $Version) { throw "DisplayVersion '$($k.DisplayVersion)' != '$Version'" }
        if ($k.InstallLocation.TrimEnd('\') -ine $Dir.TrimEnd('\')) { throw "InstallLocation '$($k.InstallLocation)'" }
        if ($k.QuietUninstallString -notmatch 'Uninstall\.exe" /silent$') { throw "QuietUninstallString '$($k.QuietUninstallString)'" }
        "DisplayVersion $($k.DisplayVersion), $($k.InstallLocation)"
    } | Out-Null
    Test-Step "$Label no shortcuts / autostart (flags honoured)" {
        $desk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GamesHub.lnk'
        $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'GamesHub.lnk'
        if (Test-Path $desk) { throw "desktop shortcut created: $desk" }
        if (Test-Path $menu) { throw "start menu shortcut created: $menu" }
        $run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue)
        if ($run -and ($run.PSObject.Properties.Name -contains 'GamesHub')) { throw "Run value set: $($run.GamesHub)" }
        'none'
    } | Out-Null
}

function Invoke-InstallerSuite([string]$SetupExe) {
    $v = Test-SetupFile $SetupExe
    $version = ($v.Informational -replace '[-+].*$', '')
    $realData = Join-Path $env:LOCALAPPDATA 'GamesHub'

    if (Test-Path $UninstallKey) {
        Add-Result 'installer: clean machine' $false "GamesHub is already registered ($((Get-ItemProperty $UninstallKey).InstallLocation)); refusing to touch it" 0
        return
    }
    $hadData = Test-Path $realData
    $games = New-GamesFixture
    $gamesBefore = Get-TreeHashes $games
    $root = New-TempDir 'install'
    $dir = Join-Path $root 'GamesHub'
    $setupArgs = @('/silent', '/dir', "`"$dir`"", '/games', "`"$games`"", '/nodesktop', '/nostartmenu')

    try {
        $installed = $null
        if (-not (Test-Step 'install #1: Setup /silent -> OK' { $script:installedDir = Invoke-Setup $SetupExe $setupArgs 'install #1'; $script:installedDir })) { return }
        $installed = $script:installedDir
        Test-Step 'install #1: result dir is the requested one' { if ($installed.TrimEnd('\') -ine $dir) { throw "$installed != $dir" } 'ok' } | Out-Null
        Test-Installed $installed $games $version 'install #1:'
        Test-Step 'install #1: settings.json seeded with gamesDir' {
            $s = Get-Content (Join-Path $realData 'settings.json') -Raw | ConvertFrom-Json
            if ($s.gamesDir -ine $games) { throw "gamesDir '$($s.gamesDir)'" } $s.gamesDir
        } | Out-Null

        # upgrade / reinstall over an existing install
        Test-Step 'install #2 (upgrade over existing): Setup /silent -> OK' { Invoke-Setup $SetupExe $setupArgs 'install #2' } | Out-Null
        Test-Installed $installed $games $version 'install #2:'
        Test-Step 'install #2: single uninstall entry' {
            $n = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | Where-Object { (Get-ItemProperty $_.PSPath).DisplayName -eq 'GamesHub' }).Count
            if ($n -ne 1) { throw "$n entries" } '1 entry'
        } | Out-Null

        # the installed app itself
        Invoke-AppSuite (Join-Path $installed 'GamesHub.exe') $games '[installed]'

        Test-Step 'uninstall: /silent removes the app' { Invoke-Uninstall $installed @('/silent') } | Out-Null
        Test-Step 'uninstall: key removed' { if (Test-Path $UninstallKey) { throw 'still present' } 'gone' } | Out-Null
        Test-Step 'uninstall: games folder untouched' {
            $diff = @(Compare-Trees $gamesBefore (Get-TreeHashes $games))
            if ($diff.Count) { throw ($diff -join '; ') } "$($gamesBefore.Count) files identical"
        } | Out-Null
        Test-Step 'uninstall: user data kept without /purge' {
            if (-not (Test-Path (Join-Path $realData 'settings.json'))) { throw "$realData\settings.json deleted" } 'kept'
        } | Out-Null

        # purge path
        Test-Step 'install #3 (for purge): Setup /silent -> OK' { Invoke-Setup $SetupExe $setupArgs 'install #3' } | Out-Null
        Test-Step 'uninstall /purge: removes app and user data' {
            Invoke-Uninstall $installed @('/silent', '/purge') | Out-Null
            if (Test-Path $realData) { throw "$realData still exists" }
            if (Test-Path $UninstallKey) { throw 'key still present' }
            'app, key and user data removed'
        } | Out-Null
        Test-Step 'uninstall /purge: games folder untouched' {
            $diff = @(Compare-Trees $gamesBefore (Get-TreeHashes $games))
            if ($diff.Count) { throw ($diff -join '; ') } 'identical'
        } | Out-Null
    } finally {
        # never leave a registered install behind on the runner
        if ((Test-Path $UninstallKey) -and (Test-Path (Join-Path $dir 'Uninstall.exe'))) {
            try { Invoke-Uninstall $dir @('/silent') | Out-Null } catch { Write-Warning "cleanup uninstall failed: $($_.Exception.Message)" }
        }
        if (-not $hadData -and (Test-Path $realData) -and $env:GITHUB_ACTIONS -eq 'true') { Remove-DirRetry $realData }
    }
}

# ====================================================================== main

New-Item -ItemType Directory -Force $OutDir | Out-Null
Get-ChildItem -LiteralPath $OutDir -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Write-Host "GamesHub E2E - mode $Mode" -ForegroundColor White
$exit = 0
try {
    if ($Mode -eq 'App') {
        $exe = Join-Path $AppDir 'GamesHub.exe'
        if (-not (Test-Path -LiteralPath $exe)) { throw "GamesHub.exe not found in $AppDir (run ./build.ps1 first)" }
        if (-not $NoSnapshot) {
            # a private copy: a concurrent ./build.ps1 can't swap files under the running app, and nothing we run can dirty dist/app
            $snap = Join-Path (New-TempDir 'app') 'app'
            Copy-Item -LiteralPath $AppDir -Destination $snap -Recurse
            $exe = Join-Path $snap 'GamesHub.exe'
            Write-Host "App snapshot: $snap" -ForegroundColor DarkGray
        }
        Backup-JumpList
        $games = New-GamesFixture
        $gamesBefore = Get-TreeHashes $games
        Invoke-AppSuite $exe $games ''
        Test-Step 'fixture: games folder untouched by the app' {
            $diff = @(Compare-Trees $gamesBefore (Get-TreeHashes $games))
            if ($diff.Count) { throw ($diff -join '; ') } "$($gamesBefore.Count) files identical"
        } | Out-Null
    } else {
        if (-not $Setup) {
            $Setup = @(Get-ChildItem (Join-Path $RepoRoot 'dist\package') -Filter 'GamesHub-Setup-*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName)
            if (-not $Setup) { throw '-Setup <path to GamesHub-Setup-x.y.z.exe> is required (or build with ./build.ps1 -Package)' }
        }
        $Setup = [IO.Path]::GetFullPath($Setup)
        if (-not (Test-Path -LiteralPath $Setup)) { throw "Setup not found: $Setup" }
        $allowed = ($env:GITHUB_ACTIONS -eq 'true') -or $IUnderstandThisModifiesThisMachine
        if ($DryRun) {
            Write-Host "Dry run: nothing will be installed. CI guard would $(if ($allowed) { 'ALLOW' } else { 'REFUSE' }) a real run here." -ForegroundColor Yellow
            Write-Host "  Setup  $Setup`n  AppDir $AppDir (reference file list)`n  Plan   verify version info -> install /silent /dir <tmp> /games <fixture> /nodesktop /nostartmenu" `
                "-> verify files/registry -> install again (upgrade) -> App suite on installed exe -> Uninstall /silent" `
                "-> verify removal, games untouched, data kept -> install -> Uninstall /silent /purge -> data removed"
            Test-SetupFile $Setup | Out-Null
            Add-Result 'installer: guard' $true "real run $(if ($allowed) { 'allowed' } else { 'refused' }) on this machine (GITHUB_ACTIONS='$env:GITHUB_ACTIONS')" 0
        } elseif (-not $allowed) {
            Write-Host 'REFUSED: Installer mode installs/uninstalls GamesHub, writes HKCU and %LOCALAPPDATA%\GamesHub.' -ForegroundColor Red
            Write-Host 'It only runs on CI (GITHUB_ACTIONS=true). Use -DryRun to validate the plan locally.' -ForegroundColor Red
            Add-Result 'installer: guard' $false 'refused: not running on GitHub Actions' 0
            $exit = 3
        } else {
            Invoke-InstallerSuite $Setup
        }
    }
} catch {
    Add-Result 'harness' $false ($_.Exception.Message + ' @ ' + $_.InvocationInfo.PositionMessage) 0
} finally {
    Stop-Owned @($script:Owned.Keys)
    Restore-JumpList
    if (-not $KeepTemp) { foreach ($d in $script:TempDirs) { Remove-DirRetry $d } }
    else { Write-Host "Temp dirs kept: $($script:TempDirs -join ', ')" -ForegroundColor DarkGray }
}

$failed = @($script:Results | Where-Object { -not $_.pass })
Write-Host ''
$script:Results | Format-Table @{ n = 'Result'; e = { if ($_.pass) { 'PASS' } else { 'FAIL' } } }, @{ n = 'Check'; e = { $_.name } }, @{ n = 'ms'; e = { $_.ms } }, @{ n = 'Detail'; e = { $_.detail } } -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$summary = [ordered]@{
    mode = $Mode; startedAt = (Get-Date).AddMilliseconds(-$Clock.ElapsedMilliseconds).ToString('o'); durationMs = $Clock.ElapsedMilliseconds
    passed = $script:Results.Count - $failed.Count; failed = $failed.Count; checks = $script:Results
}
[IO.File]::WriteAllText((Join-Path $OutDir 'e2e-results.json'), ($summary | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
$color = if ($failed.Count) { 'Red' } else { 'Green' }
Write-Host ("{0} passed, {1} failed in {2:N1}s -> {3}" -f ($script:Results.Count - $failed.Count), $failed.Count, $Clock.Elapsed.TotalSeconds, (Join-Path $OutDir 'e2e-results.json')) -ForegroundColor $color
if ($exit -eq 0 -and ($failed.Count -gt 0 -or $script:Results.Count -eq 0)) { $exit = 1 }
exit $exit
