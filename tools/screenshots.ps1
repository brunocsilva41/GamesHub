#Requires -Version 7
<#
.SYNOPSIS
  Regenerates the README screenshots (docs/screenshots/*.png) from the real app, fully isolated:
  a temporary data folder (GAMESHUB_DATA_DIR → own settings, cache and single-instance scope), a demo
  library of well-known Steam games, and the WebView2 DevTools protocol for navigation and capture.
  It never touches your installed GamesHub, your settings or your games folder, and sends no keystrokes.
.EXAMPLE
  pwsh tools/screenshots.ps1            # uses dist/app (run build.ps1 first)
#>
param([string]$AppDir = 'dist/app', [int]$Port = 9344)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root $AppDir 'GamesHub.exe'
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }

$work = Join-Path ([IO.Path]::GetTempPath()) ("gh-shots-" + [guid]::NewGuid().ToString('n').Substring(0, 8))
$data = Join-Path $work 'data'; $games = Join-Path $work 'Jogos'
New-Item -ItemType Directory -Force $data, $games | Out-Null
# Run a private copy so the build folder is never locked by this tool.
Copy-Item (Split-Path $exe -Parent) (Join-Path $work 'app') -Recurse
$exe = Join-Path $work 'app' 'GamesHub.exe'

# Demo library: public Steam titles (artwork comes from the public Steam CDN).
$demo = [ordered]@{
    'Counter-Strike 2' = 730; 'Dota 2' = 570; 'Portal 2' = 620; 'Stardew Valley' = 413150; 'Hades' = 1145360
    'Hollow Knight' = 367520; 'Celeste' = 504230; 'Terraria' = 105600; 'Cyberpunk 2077' = 1091500
    'ELDEN RING' = 1245620; "Baldur's Gate 3" = 1086940; 'Rocket League' = 252950; 'Forza Horizon 5' = 1551360
    'Red Dead Redemption 2' = 1174180; 'Sea of Thieves' = 1172620; 'Lethal Company' = 1966720
}
foreach ($name in $demo.Keys) {
    Set-Content (Join-Path $games "$name.url") "[InternetShortcut]`r`nURL=steam://rungameid/$($demo[$name])`r`n" -Encoding ascii
}
@{
    gamesDir = $games; importSteam = $false; importEpic = $false; importRiot = $false; importHydra = $false
    importSteamPlaytime = $false; autoArtwork = $true; fetchMetadata = $true; pcgwEnabled = $false
    checkUpdates = $false; hotkeyEnabled = $false; quickLaunchEnabled = $true; quickLaunchHotkey = 'Ctrl+Alt+Shift+F11'
    trackPlaytime = $false; automationEnabled = $false; startWithWindows = $false; closeToTray = $false
} | ConvertTo-Json | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
@{ x = 80; y = 60; width = 1360; height = 860; maximized = $false; trayHintShown = $true } |
    ConvertTo-Json | Set-Content (Join-Path $data 'window.json') -Encoding utf8

# Some play history so "Continuar jogando" and the stats look alive.
$now = Get-Date
$lib = @{ version = 1; legacyImported = $true; ignored = @(); games = @{} }
$i = 0
foreach ($name in $demo.Keys) {
    $i++
    $entry = @{ addedAt = $now.AddDays(-60 + $i).ToString('o') }
    if ($i -le 7) { $entry.lastPlayed = $now.AddHours(-3 * $i * $i).ToString('o'); $entry.playSeconds = 3600 * (40 - 4 * $i) }
    if ($i -in 2, 5, 11) { $entry.favorite = $true }
    $lib.games["folder:$($name.ToLowerInvariant()).url"] = $entry
}
$lib | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $data 'library.json') -Encoding utf8

$env:GAMESHUB_DATA_DIR = $data
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port"
$proc = Start-Process $exe -ArgumentList '--show' -PassThru
try {
    $out = Join-Path $root 'docs' 'screenshots'
    New-Item -ItemType Directory -Force $out | Out-Null
    node (Join-Path $PSScriptRoot 'screenshots.mjs') $Port $out
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot capture failed' }
}
finally {
    Start-Process $exe -ArgumentList '--quit' -Wait
    if (-not $proc.WaitForExit(10000)) { Stop-Process -Id $proc.Id -Force }
    Remove-Item Env:GAMESHUB_DATA_DIR, Env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
