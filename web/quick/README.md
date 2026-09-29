# Quick-launch palette (`web/quick`)

Hosted by `src/GamesHub/QuickLaunch/QuickLaunchForm.cs` at
`https://app.gameshub.example/quick/index.html` in its own WebView2 (shared environment via `WebViewEnv`).

| File | Purpose |
|---|---|
| `index.html`, `quick.css`, `quick.js` | The palette page (thin view — ranking is done in C# `QuickSearch`). |
| `hotkey-input.js` | Reusable `<gh-hotkey-input>` web component for the settings page (both hotkeys). |

`../css/tokens.css` is linked when present; every CSS variable has a fallback, so the page also works without it.

## Protocol

Plain JSON objects (not the main app's `cmd`/`reply` bridge). JS → C# via `chrome.webview.postMessage(obj)`,
C# → JS via `PostWebMessageAsJson`. Messages from any origin other than `https://app.gameshub.example/quick/` are ignored.

### Page → C#

| type | fields | effect |
|---|---|---|
| `ready` | — | page loaded; C# replies with `show` (if a show is pending/visible) or `reset` |
| `query` | `seq: number, q: string` | C# replies `results` with the same `seq` (empty `q` → recent games) |
| `launch` | `id` | `ILibraryService.Launch(id)` off the UI thread; hides on success, else `status` err |
| `reveal` | `id` | opens Explorer on `RevealPath(id)` (folder, or `/select,` a file) and hides |
| `hide` | — | hides the palette (Esc) |
| `log` | `level: "warn"\|"error", msg` | written to the app log |

### C# → page

| type | fields | when |
|---|---|---|
| `show` | `items: Item[]` (recent), `hotkey: string`, `reduceMotion: bool` | palette shown: clear input, render, focus + select |
| `reset` | `items: Item[]` | after hiding (so the next show never flashes stale content) and on first `ready` |
| `results` | `seq, q, items: Item[]` | answer to `query`; the page drops replies whose `seq` isn't the latest |
| `changed` | — | library changed while visible; the page re-sends its current query |
| `launching` | `id` | launch started (row shows "Iniciando…") |
| `status` | `kind: "err"\|"info", text` | pt-BR message shown in the footer |

```ts
Item = {
  id, name, platform, collections: string[], running: bool, favorite: bool, hidden: bool,
  lastPlayed: string|null /* ISO 8601 */, playSeconds: number,
  header: string|null, icon: string|null,   // https://art.gameshub.example/<rel>?v=<mtimeTicks>
  hl: [start, length][]                      // highlighted ranges in `name`
}
```

## Keyboard

`↑/↓` (wrap), `Tab/Shift+Tab`, `PageUp/PageDown` move · `Enter` launch · `Ctrl+Enter` open folder · `Esc` hide.
Focus loss hides. The list is a `listbox` with `option`s; the input is a `combobox` using `aria-activedescendant`.

## Search (C# `QuickSearch`)

Accent/case-insensitive. Tiers: exact name › name prefix › compact prefix (`halflife`) › initials (`lmsh`, `gta5` —
roman numerals I–X ↔ digits) › all tokens are word prefixes › substring › tokens matching name/platform/collections ›
in-order subsequence. Small boost (≤ ~60) for recently played, play time, favorite, running. Hidden games −300 and
excluded from the empty-query "Recentes" list (running first, then last played, favorites, play time).

## `<gh-hotkey-input>`

```html
<script type="module" src="quick/hotkey-input.js"></script>
<gh-hotkey-input value="Ctrl+Shift+Space" label="Atalho da paleta rápida"></gh-hotkey-input>
```
- Click / Enter / Space starts recording ("Pressione as teclas…"); modifiers preview live.
- `Esc` cancels, `Backspace` clears (value `""`; disable with `allow-empty="false"`).
- Combos without Ctrl/Alt/Shift/Win are rejected except F1–F24.
- Emits `change` (`bubbles`, `composed`) with `event.detail.value` = normalized string, e.g. `"Ctrl+Shift+Space"`.
- `el.value` get/set, `el.setError("O atalho … já está em uso por outro programa.")` to show a backend error.
- Named exports: `normalizeHotkey(text)`, `comboFromEvent(e)`, `keyFromEvent(e)`, `hotkeyLabels(value)`.

Format (same as C# `QuickHotkey`; CORE's `HotkeyParser` currently accepts a subset — no Enter/Tab/PrintScreen/Num0–9): `Ctrl+Alt+Shift+Win+Key`, key ∈ `A–Z 0–9 F1–F24 Space
Enter Tab Up Down Left Right Home End PageUp PageDown Insert Delete Pause PrintScreen Num0–Num9`.

## Dev mode

Without `chrome.webview` (served by any static server, e.g. `npx serve web`), `quick.js` uses a tiny in-page mock
with sample games and substring matching, only for styling.
