# GamesHub v2 — Architecture & Contracts

Single source of truth for the v2 rewrite. Five agents work **in parallel in the same working tree**.
Ownership is strict: **only edit files you own.** If you need something from another module, code
against the contract below; if the contract is insufficient, write the request in your final report
(do not edit the contract or another agent's files).

## Stack (fixed)

- C# compiled by modern Roslyn (`dotnet .../Roslyn/bincore/csc.dll -langversion:latest`) against the
  **.NET Framework 4.8** runtime assemblies. Modern syntax is OK; APIs must exist in .NET Framework 4.8
  (no `System.Text.Json`, no `HttpClient` extensions from .NET 5+, no `Span` APIs, no NuGet packages).
  Available refs: mscorlib, System, System.Core, System.Drawing, System.Windows.Forms,
  System.Web.Extensions (JavaScriptSerializer), System.Net.Http, System.Xml, System.Xml.Linq,
  Microsoft.CSharp, System.Runtime.Serialization, WebView2 (Core + WinForms, v1.0.4191).
- UI is plain HTML/CSS/JS (no frameworks, no build step, no CDN — the app works offline) hosted in WebView2.
- Namespace: `GamesHub` for everything (tests: `GamesHub.Tests`). Product name: **GamesHub**, exe: `GamesHub.exe`.
- User-facing text: **Portuguese (pt-BR) with proper accents**. Code, comments, logs: English.
- Build: `./build.ps1` (app → `dist/app`), `-Test` (runs tests), `-Package` (installer → `dist/package`).
  **Errors in files you don't own are expected while others work — ignore them; make sure yours compile.**
- Reference v1 implementation: `_archive/v1-src/GameLounge.cs`, v1 UI in git history
  (`git show HEAD~1:web/index.html` or the current `web/index.html` until WEB replaces it).

## Hard rules for all agents

1. Never run, kill or modify the installed app (`%LOCALAPPDATA%\Programs\GamesHub`, process `GamesLounge.exe`).
2. Never launch games, never run Setup.exe/Uninstall.exe, never write to the registry during development
   (write code that does, but don't execute it). Don't start the GUI app unless told to.
3. Never delete or modify anything in the user's games folder (`C:\Users\Bruno Silva\Desktop\jogos`)
   or its `_hub` data during development. Reading is fine.
4. No `catch { }` that swallows silently — at minimum `Log.Warn(...)`.
5. Don't `git commit`. The lead commits.
6. Keep it light: no new dependencies, no giant files; split code into focused files (< ~500 lines).

## Data locations (`Shared/Infrastructure.cs → AppPaths`)

| What | Where |
|---|---|
| Settings | `%LOCALAPPDATA%\GamesHub\settings.json` (CORE) |
| Library metadata (overrides, playtime, lastPlayed, ignore list, addedAt) | `%LOCALAPPDATA%\GamesHub\library.json` (LIB) |
| Artwork cache | `%LOCALAPPDATA%\GamesHub\cache\art\<key>\<kind>.<ext>` (ART) |
| Removed shortcuts | `%LOCALAPPDATA%\GamesHub\trash\` (LIB) |
| Logs | `%LOCALAPPDATA%\GamesHub\logs\gameshub.log` (use `Log.*`) |
| WebView2 profile | `%LOCALAPPDATA%\GamesHub\webview\` (CORE) |
| Legacy v1 data (read-only migration) | `<GamesDir>\_hub\` — `playlog.json` (LIB), `covers\` (ART), install `config.json` (CORE) |

## Modules & ownership

| Agent | Owns (exclusive) |
|---|---|
| **lead** | `src/GamesHub/Shared/**`, `tests/TestRunner.cs`, `docs/ARCHITECTURE.md`, `.gitignore` |
| **CORE** | `src/GamesHub/Core/**` — Program (single instance via Mutex + activation message), MainForm (WebView2 host, frameless window, virtual hosts), Bridge (protocol below), SettingsStore, TrayController (incl. recent games), WindowsIntegration (start with Windows via HKCU Run, taskbar Jump List of recent games, global hotkey), migration of v1 `config.json`. Tests: `tests/Core/**` |
| **LIB** | `src/GamesHub/Library/**` — LibraryService, sources (folder `.lnk/.url/.exe`, Steam `libraryfolders.vdf` + `appmanifest_*.acf`, Epic `.item` manifests), platform detection, metadata store (`library.json`), launcher, play-time tracker (process monitoring by InstallDir/Exe), safe trash + undo, v1 `playlog.json` import. Tests: `tests/Library/**` |
| **ART** | `src/GamesHub/Artwork/**` — ArtworkService: Steam CDN art (header/capsule/hero/logo), fuzzy Steam search for non-Steam games, optional SteamGridDB (when key set), icon extraction from exe/lnk, custom images, v1 cover import, bounded concurrent downloads, no rescan storms. Tests: `tests/Artwork/**` |
| **WEB** | `web/**` — entire UI |
| **DIST** | `src/Installer/**`, `src/GamesHub/Update/**`, `src/GamesHub/Properties/AssemblyInfo.cs`, `build.ps1`, `README.md`, `CHANGELOG.md`, `docs/USER_GUIDE.md`, `assets/**` |

Composition root (CORE, `Program.cs`):
```csharp
var settings = SettingsStore.Load();                 // CORE
var art      = new ArtworkService(settings);         // ART
var library  = new LibraryService(settings, art);    // LIB
var updater  = new UpdateChecker(settings);          // DIST
```
LIB calls `art.Resolve(game)` when building each Game and subscribes to `art.ArtworkUpdated` to refresh
that game's `Art` and raise its own `Changed`. Calling `art.ImportLegacy(AppPaths.LegacyHubDir(settings.GamesDir))`
is **CORE's** job (once, at startup, before `library.Start()`). CORE never touches files owned by LIB/ART directly.

## WebView2 hosting (CORE)

- `SetVirtualHostNameToFolderMapping("app.gameshub.example", AppPaths.WebDir, Allow)` → UI at `https://app.gameshub.example/index.html`
- `SetVirtualHostNameToFolderMapping("art.gameshub.example", AppPaths.ArtDir, Allow)` → artwork
- **No HttpListener, no ports, no polling.** Communication is only via `window.chrome.webview`.
- Navigation to any other origin is cancelled; external `https:` links go through `openExternal`.
- Dev tools/context menu/zoom disabled in release; `--debug` command-line flag enables dev tools.
- Window: frameless (UI draws its title bar; `-webkit-app-region: drag` works via
  `IsNonClientRegionSupportEnabled = true`), rounded corners (DWM), min size 900×600, remembers
  size/position/maximized in `%LOCALAPPDATA%\GamesHub\window.json`.

## Bridge protocol (CORE implements C# side, WEB implements JS side)

All messages are JSON objects. Transport: `chrome.webview.postMessage(obj)` (JS→C#) and
`CoreWebView2.PostWebMessageAsJson(json)` (C#→JS).

### Request / reply (JS → C#)
```json
{ "type": "cmd", "id": 17, "name": "launch", "args": { "id": "steam:730" } }
```
C# always answers exactly once:
```json
{ "type": "reply", "id": 17, "ok": true, "data": { ... } }
{ "type": "reply", "id": 17, "ok": false, "error": "Mensagem em pt-BR" }
```
When a command returns an `OpResult`, `data` is `{ "message": "...", "undoToken": "...|null", "gameId": "...|null" }`
and `ok` mirrors `OpResult.Ok` (`error` = `OpResult.Message` when not ok).

### Events (C# → JS)
```json
{ "type": "event", "name": "<name>", "data": { ... } }
```

| Event | data | When |
|---|---|---|
| `games` | `{ games: GameDto[], collections: string[] }` | library `Changed` (CORE throttles to ≤ 4/s) |
| `running` | `{ id, running: bool }` | LIB `RunningChanged` |
| `settings` | `SettingsDto` | after settings change |
| `toast` | `{ text, kind: "ok"\|"err"\|"info", undoToken?: string }` | backend-originated notices |
| `update` | `{ version, notes, pageUrl }` | update available (startup check) |
| `updateProgress` | `{ percent }` | during download |
| `navigate` | `{ view: "library"\|"settings"\|"game", id?: string }` | tray/jump-list/hotkey actions |
| `focusSearch` | `{}` | global hotkey pressed while visible |
| `windowState` | `{ maximized: bool, fullscreen: bool, pinned: bool }` | window state changes |

### Commands (JS → C#)

| name | args | reply data |
|---|---|---|
| `getState` | — | `{ games: GameDto[], collections: string[], settings: SettingsDto, version: string, windowState: {...}, demo: false }` |
| `launch` | `{ id }` | OpResult |
| `reveal` | `{ id }` | OpResult (opens Explorer on `RevealPath`) |
| `remove` | `{ id }` | OpResult (with `undoToken`) |
| `undo` | `{ token }` | OpResult |
| `updateGame` | `{ id, edit: { name?, launchArgs?, steamAppId?, favorite?, hidden?, collections? } }` | OpResult |
| `addFiles` | `{}` + **additional objects** = dropped `File`s (via `chrome.webview.postMessageWithAdditionalObjects`) — C# reads `CoreWebView2File.Path`; `.exe/.lnk/.url` → `AddFromFile` | `{ results: OpResult[] }` |
| `pickFile` | `{}` | opens native OpenFileDialog (exe/lnk/url) → `AddFromFile` → OpResult (or `{cancelled:true}`) |
| `addSteam` | `{ appId, name? }` | OpResult |
| `searchSteam` | `{ query }` | `{ results: [{ appId, name, iconUrl }] }` |
| `setArt` | `{ id, kind }` + optional one additional `File` (dropped image). Without a file → native image picker | OpResult |
| `clearArt` | `{ id, kind }` | OpResult |
| `refreshArt` | `{ id }` | OpResult |
| `rescan` | — | OpResult |
| `setSettings` | `{ patch: Partial<SettingsDto> }` | `SettingsDto` (applies side effects: autostart, hotkey, rescan when sources change) |
| `pickGamesDir` | — | `SettingsDto` or `{cancelled:true}` |
| `window` | `{ action: "minimize"\|"maximize"\|"restore"\|"close"\|"pin"\|"fullscreen", on?: bool }` | `windowState` |
| `openExternal` | `{ url }` (https only) | `{}` |
| `openDataFolder` | — | `{}` (opens `%LOCALAPPDATA%\GamesHub`) |
| `checkUpdate` | — | `{ available, version, notes, pageUrl }` |
| `installUpdate` | — | `{ started: bool }` |
| `log` | `{ level: "info"\|"warn"\|"error", msg }` | `{}` |
| `quit` | — | `{}` (really exits, bypassing close-to-tray) |

`close` minimizes to tray when `settings.closeToTray`, otherwise exits.
After `launch` succeeds, CORE applies `settings.onLaunch` ("tray" hides window, "minimize", "none").

### DTO shapes (camelCase JSON)

```ts
GameDto = {
  id, name, platform, source, ext, filePath, installDir, launchArgs, steamAppId,
  favorite: bool, hidden: bool, collections: string[],
  lastPlayed: string|null /* ISO 8601 */, playSeconds: number, addedAt: string /* ISO */,
  running: bool,
  art: { header, capsule, hero, logo, icon }  // absolute URLs "https://art.gameshub.example/<rel>?v=<mtimeTicks>" or null
}
SettingsDto = AppSettings fields in camelCase (gamesDir, importSteam, importEpic, onLaunch, startWithWindows,
  startMinimized, closeToTray, hotkeyEnabled, hotkey, sortBy, view, cardStyle, reduceMotion, autoArtwork,
  steamGridDbKey, trackPlaytime, checkUpdates, updateRepo, language)
```

## Dev mode for the UI (WEB)

When `window.chrome?.webview` is absent (page opened directly in a browser), the UI uses an in-page
mock bridge (`web/js/mock.js`) with realistic sample data so it can be developed/screenshotted
standalone, and shows a small "Modo demonstração" badge. The mock is never used inside the app.
