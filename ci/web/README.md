# Web-UI quality gates

Two gates for `web/`. Both need only Node.js ≥ 20 (20.11+ recommended). There are no npm dependencies,
nothing to install, and they run the same on Windows and Linux.

```sh
node ci/web/lint.mjs                 # static checks            (~1.3 s)
node tests/web/run.mjs               # unit/contract tests      (~5.5 s, any Node >= 20)
node --test "tests/web/*.test.mjs"   # same tests, Node >= 21 (Node expands the quoted glob)
```

`node --test tests/web/` (a directory argument) only works on Node 20. From Node 22 on, `--test`
arguments are files or globs, so use one of the forms above.

CI lines (fail the job on a non-zero exit):

```yaml
- run: node ci/web/lint.mjs
- run: node tests/web/run.mjs
```

## `ci/web/lint.mjs`

Output is `file:line: [check] message`, followed by a summary line. The exit code is 0 when the
lint is clean (warnings are allowed), 1 when there is at least one error, and 2 when the linter
itself crashed.

Flags:
- `--root <dir>` lints another checkout, for example a broken copy.
- `--quiet` hides the info lines.
- `--json` prints machine-readable output.
- `--no-baseline` ignores `baseline.json`.

| check | guards against |
|---|---|
| `parse` | Syntax errors. Each `web/**/*.js` file is copied to a temp `.mjs` file and checked with `node --check`, in parallel. This parses it as an ES module on every Node version. |
| `imports` | Broken relative `import`/`export … from`/`import('…')` paths, bare or URL specifiers (there is no bundler and no CDN), named imports the target does not export (`export *` is followed), and `NS.member` uses of `import * as NS` where `member` is not exported. |
| `refs` | `src`/`href` in HTML and `url()`/`@import` in CSS that point to missing files, point outside `web/`, or are non-local. |
| `urls` | Any `http://` URL, except XML namespace identifiers like `http://www.w3.org/2000/svg`. Also any `https://` host that is not in the allowlist (`ALLOWED_HOSTS` in `lib/static-checks.mjs`). The allowlist holds the app's virtual hosts (`app./art.gameshub.example`) and the user-facing links opened via `openExternal` (`www.pcgamingwiki.com`, `www.steamgriddb.com`). `DEMO_HOSTS` lists sample-data sites that are tolerated only in `js/mock*.js`. |
| `hygiene` | `console.log/debug/trace/dir/table(` and `debugger` in code (comments and strings are ignored). Also `TODO`/`FIXME`/`XXX` without an issue reference: `TODO(#12)` and `ABC-12` are accepted. |
| `budget` | JS modules over 300 lines, CSS files over 700 lines, and a total `web/` size over 1.5 MB. The summary prints the largest files and any file above 90 % of its line budget. |
| `html` | Every `*.html` file must have `<html lang="pt-BR">` and `<meta charset>`. The check also flags `on*=` inline handlers, inline `<script>` (CSP), `<img>` without `alt`, and `<button>` without text or `aria-label`. Markup inside JS string/template literals is checked for inline handlers and for `<img>` without `alt`. |
| `bridge` | UI ↔ C# ↔ mock contract, described below. |

### Bridge cross-check (`lib/contract.mjs`)

- **UI commands.** The lint collects every `bridge.call('x', …)`, including calls made through
  wrappers. Wrappers are discovered automatically: `run(name, …)` in `actions.js` forwards its
  `name` parameter to `bridge.call`, so every `run('x')` / `A.run('x')` counts, and so does
  `call('x')` in `actions2.js`, transitively. A command name that is not a string literal is an
  error, because it cannot be verified.
- **C# commands.** The lint reads the `case "x":` labels at the top level of `switch (name)` in
  `src/GamesHub/Core/BridgeCommands*.cs`. The C# scanner ignores comments and strings, and nested
  switches are skipped.
- **Mock commands.** These come from `createMockHost().commands` at runtime, so they are exactly
  what demo mode serves. If `mock.js` cannot be imported, that is an error, and the lint falls back
  to a static read of the command tables.
- **Errors:**
  - The UI sends a command that C# lacks.
  - The UI sends a command that the mock lacks.
  - C# has a command that the mock lacks (demo mode would break).
  - The mock has a command that C# lacks.
  - `TIMEOUTS` in `bridge.js` has a key that is not a command.
- **Events:** the UI's `bridge.on('x')` and the mock's `event('x')` must match a C# `Emit("x")`.
  C# events that nobody listens to are only warnings.
- **Quick-launch page:** each `send({ type })` in `web/quick/quick.js` must match a `case` in
  `QuickLaunchController`. Each `type = "x"` posted by C# must match a `case 'x'` in quick.js.

### Baseline

`ci/web/baseline.json` lists known issues in code this gate does not own. Each entry downgrades
matching errors to warnings. An entry that no longer matches becomes a "stale entry" warning, so
the baseline can only shrink. Do not add entries for new code.

### Limits

The lint is regex over tokenized source, not a full parser:
- Regex literals are told apart from division by looking at the previous token.
- Named exports declared as `export const a = 1, b = 2` expose only `a`.
- Array-destructuring exports are not seen.
- A bridge wrapper is recognized only when its signature is within 400 characters before the
  forwarding call.

`tests/web/contract.test.mjs` unit-tests the scanners on the tricky cases.

## `tests/web/`

`node:test` + `node:assert/strict` only. The tests import the pure UI modules directly: `store`,
`util`, `emitter`, `bridge`, `mock*` and `automation-model`. `_setup.mjs` provides:
- a fake `chrome.webview` transport;
- tiny `HTMLElement`/`customElements` stubs, used for `quick/hotkey-input.js`;
- `unrefLongTimers()`, which unrefs the mock's long demo timers so test files exit promptly;
- C# source readers, used to compare shapes with the real DTOs instead of hand-copied lists.

| file | covers |
|---|---|
| `store.test.mjs` | Sections, platform/collection filters, genre and quick filters (never/stale/installed), accent-insensitive multi-word search across name/platform/collection/genre, every sort including the client-side `size`, counts, featured pick, and store change events. |
| `util.test.mjs` | Duration, relative date (pt-BR), bytes and dates; `normalize`/`esc`/`monogram`; FNV hash and gradient determinism; `debounce`. |
| `automation-model.test.mjs` | Normalization, payload, per-type validation, move, and equality. Action types must match the mock. |
| `bridge.test.mjs` | Request/reply correlation, errors, JSON-string messages, explicit/default/per-command/zero timeouts (mock timers), transport failures, files, `log()`, events, and the mock fallback. |
| `mock.test.mjs` | Every mock command, run through the real bridge. The case table must equal both the mock list and the C# list. Reply shapes are compared with the C# DTO classes and dictionaries. Also covers the undo flows, hotkey validation, the taken-hotkey toast, variants group/ungroup/label/primary/suggest/dismiss, `cleanupBroken` + undo, `openPath` allowlist, automation and add flows. |
| `contract.test.mjs` | `DEFAULT_SETTINGS` against the `AppSettings` fields and literal defaults, `SERVER_SORTS` against `SortByValues`, and the hotkey grammar of the web component against the mock/SettingsStore. Also unit tests of the lint scanners. |
| `perf.test.mjs` | A 300-game library: filter + search + sort must stay under 50 ms (worst of 10 runs) for 8 scenarios, plus the sidebar aggregates. |
| `lint.test.mjs` | The lint passes on the repo and the baseline is minimal. On a deliberately broken copy in the OS temp dir, it must catch one or more injected faults per check without false positives, and the CLI must exit 1. |
