# GamesHub end-to-end smoke tests

`Invoke-E2E.ps1` starts the real `GamesHub.exe`, drives its UI through the WebView2 DevTools protocol (`cdp.mjs`,
Node >= 22, no npm packages) and, in CI, installs, upgrades and uninstalls the real `GamesHub-Setup-x.y.z.exe`.

```powershell
./build.ps1 -Package                                          # dist/app + dist/package
powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode App   # safe on a dev PC
powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode Installer -Setup dist/package/GamesHub-Setup-2.0.0.exe   # CI only
powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode Installer -Setup dist/package/GamesHub-Setup-2.0.0.exe -DryRun   # anywhere
```

| Parameter | Default | |
|---|---|---|
| `-Mode` | `App` | `App` or `Installer` |
| `-AppDir` | `dist/app` | build under test (App mode) / reference file list (Installer mode) |
| `-Setup` | first `dist/package/GamesHub-Setup-*.exe` | installer (Installer mode) |
| `-OutDir` | `dist/e2e` | `e2e-results.json`, screenshots, app log, `setup.log`/`uninstall.log` |
| `-TimeoutSec` | 90 | startup / UI readiness / installer timeouts |
| `-MemoryBudgetMB` / `-HostMemoryBudgetMB` | 600 / 150 | working set of GamesHub.exe + its WebView2 tree / GamesHub.exe alone |
| `-RequireSignature` | off | fail when Setup is not Authenticode-signed |
| `-NoSnapshot` | off | run straight from `-AppDir` (default: from a private temp copy) |
| `-KeepTemp` | off | keep fixture/data/snapshot temp folders for debugging |
| `-DryRun` | off | Installer mode: print the plan, check the Setup file, never install |

Exit code: `0` all checks passed, `1` any failure, `3` Installer mode refused (not on CI).
Results: a table on the console and `OutDir/e2e-results.json` (`{ mode, durationMs, passed, failed, checks: [{ name, pass, detail, ms }] }`).

## Fixture

A temp games folder with 5 entries: `Counter-Strike 2.url` (`steam://rungameid/730`), `Dota 2.url` (570),
`Notepad Adventure.lnk` → `notepad.exe`, `Tiny Quest.exe` (a copy of `whoami.exe`) and `Ghost Game.lnk` → a missing exe.
A temp data folder (`GAMESHUB_DATA_DIR`) gets a `settings.json` that turns off everything non-deterministic or global:
artwork/metadata/PCGW downloads, update checks, Steam/Epic/Riot/Hydra import, play-time tracking, automation, global
hotkey, quick-launch hotkey, autostart. Nothing is ever launched.

## What App mode checks

| Area | Checks |
|---|---|
| Process | no stale instance of the exe, starts and stays alive, alive after the UI run |
| Single instance | a 2nd launch (same data dir) exits 0, the log says `Activation from another instance`, still 1 process and 1 window |
| UI (CDP) | store `status === 'ready'` with the real bridge (not the mock); 5 games in the store and 5 cards in the DOM; exactly the Ghost shortcut is `broken` (card `.is-broken`, details warning panel); details → settings → library via `/js/actions.js` with DOM landmarks (`.details-main`, `.hero-title`, `Configurações`); a page reload boots cleanly again |
| Bridge | `getState` (5 games, offline settings applied, `demo: false`), `getDrives`, `rescan` (count stable), unknown command → clean `failed` reply |
| Art host | an extracted icon URL (`https://art.gameshub.example/...`) answers HTTP 200 with bytes |
| Errors | no `Runtime.exceptionThrown`, `console.error`/`assert` or `Log` errors for the whole page lifetime (buffered messages are replayed on attach) |
| Memory | working set of GamesHub.exe and of its WebView2 process tree, reported and budgeted |
| Exit | `GamesHub.exe --quit` → app exits with code 0 within 10 s, WebView2 children gone, log has `Exited cleanly` and no `ERROR` lines |
| Isolation | app folder unchanged, games folder hashes unchanged |

Screenshots: `library.png`, `details.png`, `settings.png` (Installer mode adds `installed-*` copies).

### Why it is safe on a developer PC

* `GAMESHUB_DATA_DIR` isolates settings/library/cache/logs/WebView2 profile **and** the single-instance mutex/pipe, so the
  test never talks to (or is blocked by) a running GamesHub. The env vars are set on the child `ProcessStartInfo` only.
* The UI is observed only through the DevTools port of the process started here
  (`GAMESHUB_DEVTOOLS_PORT=<free port>`, honoured only by isolated instances); no keystrokes, no mouse, no desktop capture.
* Only processes started by the script (and their WebView2 children) are ever killed, matched by PID **and** start time.
* The app writes the taskbar Jump List for AppUserModelID `GamesHub.App`, which a real install shares. The script backs up
  every `CustomDestinations` file that mentions `GamesHub.exe` and restores it after the run.
* The app runs from a private temp copy of `dist/app` (a concurrent `build.ps1` can't swap files under it).
* The test window does appear on screen for ~8 s (WebView2 must render for screenshots).

## What Installer mode checks (GitHub Actions only)

Refuses to run unless `GITHUB_ACTIONS=true` (or `-IUnderstandThisModifiesThisMachine`), and refuses if a GamesHub install is
already registered in HKCU.

1. Setup file: `FileVersion` == `AssemblyFileVersion` and `ProductVersion` == `AssemblyInformationalVersion` from
   `src/GamesHub/Properties/AssemblyInfo.cs`, version in the file name, `.sha256` matches, Authenticode status reported.
2. `Setup /silent /dir <tmp>\GamesHub /games <fixture> /nodesktop /nostartmenu` → exit 0, `%TEMP%\GamesHub\setup-result.txt`
   = `OK:<dir>`; installed files == `dist/app` (names + SHA-256; plus `install-files.txt` listing them); HKCU uninstall
   key with `DisplayVersion`, `InstallLocation`, `QuietUninstallString`; no shortcuts / Run value; `settings.json` seeded.
3. Same Setup again (**upgrade over an existing install**) → same assertions, a single uninstall entry.
4. The whole App suite against the **installed** exe (isolated data dir).
5. `Uninstall.exe /silent` → exit 0, `uninstall-result.txt` = `OK`. Uninstall runs in place and then schedules
   `cmd /c ping -n 3 & del Uninstall.exe & rmdir <dir>`, so the script polls up to 20 s for the folder to disappear.
   Then: uninstall key gone, fixture untouched, `%LOCALAPPDATA%\GamesHub\settings.json` kept.
6. Install again + `Uninstall.exe /silent /purge` → user data removed too, fixture still untouched.

## CI (GitHub Actions, `windows-latest`)

```yaml
      - name: Build
        run: ./build.ps1 -Test -Package
        shell: powershell
      - uses: actions/setup-node@v4
        with: { node-version: 22 }
      - name: E2E (app)
        run: powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode App -OutDir dist/e2e/app
      - name: E2E (installer)
        run: powershell -NoProfile -File ci/e2e/Invoke-E2E.ps1 -Mode Installer -Setup (Get-Item dist/package/GamesHub-Setup-*.exe).FullName -OutDir dist/e2e/installer
        shell: powershell
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: e2e, path: dist/e2e }
```

The WebView2 Runtime is preinstalled on `windows-latest`. Typical duration: App mode ~9 s, Installer mode ~30 s.
