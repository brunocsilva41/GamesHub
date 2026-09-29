// Bridge contract extraction: which commands/events the UI uses, which the C# host implements, and
// which the dev-mode mock implements. Pure functions over source text (+ one runtime import of mock.js).
import { pathToFileURL } from 'node:url';
import { scanJs, scanCs, lineIndex, matchBrace, braceDepths } from './scan.mjs';

const KEYWORDS = new Set(['if', 'for', 'while', 'switch', 'catch', 'with', 'function', 'return']);
const THIS_ARGS = new Set(['this', 'null', 'undefined', 'globalThis', 'window']);

/**
 * UI command names. `files` = Map(path -> src) of UI modules (exclude mock*.js).
 * Wrappers are discovered: `bridge.call(name, ...)` inside `function run(name, ...)` makes `run` a wrapper,
 * and so on transitively. Returns { commands: Map(name -> [{file,line}]), dynamic: [{file,line,arg}], wrappers }.
 */
export function uiCommands(files) {
  const scanned = [...files].map(([file, src]) => ({ file, src, ...scanJs(src), line: lineIndex(src) }));
  const wrappers = new Set(['call']);
  const commands = new Map();
  let dynamic = [];
  for (let pass = 0; pass < 5; pass++) {
    const before = wrappers.size;
    commands.clear();
    dynamic = [];
    const names = [...wrappers].map((w) => w.replace(/\$/g, '\\$')).join('|');
    const re = new RegExp(`(?<![\\w$.])((?:[\\w$]+\\??\\.)*(?:getBridge\\(\\)\\??\\.)?)(${names})\\s*\\(`, 'g');
    for (const s of scanned) {
      for (const m of s.bare.matchAll(re)) {
        const open = m.index + m[0].length - 1;
        const close = s.bare.indexOf(')', open);
        const after = close >= 0 ? s.bare.slice(close + 1).match(/^\s*(\S)/) : null;
        const before2 = s.bare.slice(Math.max(0, m.index - 20), m.index);
        if (/function\s*\*?\s*$/.test(before2) || (after && after[1] === '{' && !m[1])) continue; // definition
        const rest = s.code.slice(open + 1, open + 200);
        const lit = /^\s*(['"`])([\w.-]+)\1\s*[,)]/.exec(rest);
        const line = s.line(m.index);
        if (lit) {
          if (!commands.has(lit[2])) commands.set(lit[2], []);
          commands.get(lit[2]).push({ file: s.file, line });
          continue;
        }
        const arg = (/^\s*([^,)]*)/.exec(rest)?.[1] || '').trim();
        if (m[2] === 'call' && m[1] && THIS_ARGS.has(arg)) continue; // Function.prototype.call
        if (!arg) continue; // call() without args: not a bridge command
        // A wrapper definition forwarding its own parameter? Look back a few lines for its signature.
        const lookback = s.bare.slice(Math.max(0, m.index - 400), m.index);
        const id = arg.replace(/\$/g, '\\$');
        const def = new RegExp(`(?:function\\s+([\\w$]+)\\s*\\(\\s*${id}\\b|(?:const|let|var)\\s+([\\w$]+)\\s*=\\s*(?:async\\s*)?\\(\\s*${id}\\b|([\\w$]+)\\s*\\(\\s*${id}\\b[^)]*\\)\\s*\\{)`, 'g');
        const defs = [...lookback.matchAll(def)];
        const d = defs.length ? defs[defs.length - 1] : null;
        const wrapperName = d && (d[1] || d[2] || d[3]);
        if (wrapperName && !KEYWORDS.has(wrapperName) && /^[\w$]+$/.test(arg)) wrappers.add(wrapperName);
        else dynamic.push({ file: s.file, line, arg });
      }
    }
    if (wrappers.size === before) break;
  }
  return { commands, dynamic, wrappers: [...wrappers] };
}

/** Event names the UI subscribes to via `bridge.on('x'` / `bridge?.on('x'` / `getBridge().on('x'`. */
export function uiEvents(files) {
  const out = new Map();
  const re = /(?<![\w$.])(?:[\w$]+\??\.)*(?:bridge|getBridge\(\))\??\.on\(\s*(['"])(\w+)\1/gi;
  for (const [file, src] of files) {
    const { code, bare } = scanJs(src);
    const line = lineIndex(src);
    for (const m of code.matchAll(re)) {
      if (bare[m.index] !== code[m.index]) continue;
      if (!out.has(m[2])) out.set(m[2], []);
      out.get(m[2]).push({ file, line: line(m.index) });
    }
  }
  return out;
}

/** `case "x":` labels directly inside `switch (<expr>)` blocks matching `switchRe` (on the bare view). */
export function csSwitchCases(src, switchRe) {
  const { code, bare } = scanCs(src);
  const line = lineIndex(src);
  const depths = braceDepths(bare);
  const out = [];
  for (const m of code.matchAll(new RegExp(switchRe.source, 'g'))) {
    if (bare[m.index] !== code[m.index]) continue;
    const open = bare.indexOf('{', m.index + m[0].length - 1);
    if (open < 0) continue;
    const close = matchBrace(bare, open);
    if (close < 0) continue;
    const inner = depths[open] + 1;
    const re = /\bcase\s+"([^"\\]+)"\s*:/g;
    re.lastIndex = open;
    for (let c = re.exec(code); c && c.index < close; c = re.exec(code)) {
      if (depths[c.index] === inner) out.push({ name: c[1], line: line(c.index) });
    }
  }
  return out;
}

/** Commands dispatched by the C# Bridge (`switch (name)` in BridgeCommands*.cs). Map(name -> {file,line}). */
export function csCommands(files) {
  const out = new Map();
  for (const [file, src] of files) {
    for (const c of csSwitchCases(src, /switch\s*\(\s*name\s*\)/)) if (!out.has(c.name)) out.set(c.name, { file, line: c.line });
  }
  return out;
}

/** Matches of `re` (group 1 = name) on the code view of C# files, skipping hits inside literals/comments. */
export function csMatches(files, re) {
  const out = new Map();
  for (const [file, src] of files) {
    const { code, bare } = scanCs(src);
    const line = lineIndex(src);
    for (const m of code.matchAll(new RegExp(re.source, 'g'))) {
      if (bare[m.index] !== code[m.index]) continue;
      if (!out.has(m[1])) out.set(m[1], { file, line: line(m.index) });
    }
  }
  return out;
}

/** String-literal names matched by `re` (group 1 = quote, group 2 = name) in JS code (not comments). */
export function jsMatches(files, re) {
  const out = new Map();
  for (const [file, src] of files) {
    const { code, bare } = scanJs(src);
    const line = lineIndex(src);
    for (const m of code.matchAll(new RegExp(re.source, 'g'))) {
      if (bare[m.index] !== code[m.index]) continue;
      if (!out.has(m[2])) out.set(m[2], { file, line: line(m.index) });
    }
  }
  return out;
}

/** Keys of `const TIMEOUTS = { ... }` in bridge.js. */
export function timeoutKeys(src) {
  const { bare } = scanJs(src);
  const line = lineIndex(src);
  const m = /const\s+TIMEOUTS\s*=\s*\{/.exec(bare);
  if (!m) return new Map();
  const open = m.index + m[0].length - 1;
  const close = matchBrace(bare, open);
  const out = new Map();
  const re = /([\w$]+)\s*:/g;
  const body = bare.slice(open + 1, close);
  for (const k of body.matchAll(re)) out.set(k[1], { line: line(open + 1 + k.index) });
  return out;
}

/** Quick-launch page protocol: types the page sends and the `case '...'` types its onMessage handles. */
export function quickPageProtocol(src) {
  const { code, bare } = scanJs(src);
  const line = lineIndex(src);
  const sends = new Map();
  for (const m of code.matchAll(/(?<![\w$.])send\(\s*\{\s*type\s*:\s*([^,}]+)/g)) {
    if (bare[m.index] !== code[m.index]) continue;
    for (const s of m[1].matchAll(/(['"])(\w+)\1/g)) if (!sends.has(s[2])) sends.set(s[2], { line: line(m.index) });
  }
  const handles = new Map();
  const sw = /switch\s*\(\s*msg\.type\s*\)\s*\{/.exec(bare);
  if (sw) {
    const open = sw.index + sw[0].length - 1;
    const close = matchBrace(bare, open);
    const depths = braceDepths(bare);
    const re = /\bcase\s+(['"])(\w+)\1\s*:/g;
    re.lastIndex = open;
    for (let c = re.exec(code); c && c.index < close; c = re.exec(code)) {
      if (depths[c.index] === depths[open] + 1) handles.set(c[2], { line: line(c.index) });
    }
  }
  return { sends, handles };
}

/**
 * web/ has no package.json (no npm, by design), so Node 22+ warns MODULE_TYPELESS_PACKAGE_JSON when a
 * parent folder has a package.json without "type". The warning is noise for us: drop only that code.
 */
export async function withQuietModuleWarnings(fn) {
  const original = process.emitWarning;
  process.emitWarning = function quiet(warning, ...rest) {
    const code = typeof rest[0] === 'object' ? rest[0]?.code : rest[1];
    if (code === 'MODULE_TYPELESS_PACKAGE_JSON' || /Module type of .* is not specified/.test(String(warning?.message ?? warning))) return;
    return original.call(process, warning, ...rest);
  };
  try { return await fn(); } finally { process.emitWarning = original; }
}

/** Runtime list of mock commands (createMockHost().commands). Throws when mock.js cannot be loaded. */
export async function mockCommands(mockFile) {
  const mod = await withQuietModuleWarnings(() => import(`${pathToFileURL(mockFile).href}?lint=${Date.now()}`));
  const host = mod.createMockHost();
  if (!Array.isArray(host.commands)) throw new Error('createMockHost().commands is not an array');
  return new Set(host.commands);
}

/**
 * Static fallback (used when mock.js cannot be imported): keys of `const commands = { ... }` and of the
 * object returned at the top level of every `export function xxxCommands(...)` in the mock files.
 */
export function staticMockCommands(files) {
  const out = new Set();
  const keysAt = (bare, open) => {
    const close = matchBrace(bare, open);
    const depths = braceDepths(bare);
    const re = /(?:^|[,{\n])\s*(?:async\s+)?([\w$]+)\s*(?:\(|:\s*(?:async\s*)?(?:\([^)]*\)|[\w$]+)\s*=>)/g;
    re.lastIndex = open;
    for (let m = re.exec(bare); m && m.index < close; m = re.exec(bare)) {
      const at = m.index + m[0].indexOf(m[1]);
      if (depths[at] === depths[open] + 1) out.add(m[1]);
    }
  };
  for (const [, src] of files) {
    const { bare } = scanJs(src);
    const depths = braceDepths(bare);
    const cmd = /const\s+commands\s*=\s*\{/.exec(bare);
    if (cmd) keysAt(bare, cmd.index + cmd[0].length - 1);
    for (const fn of bare.matchAll(/export\s+function\s+\w*Commands\s*\([^)]*\)\s*\{/g)) {
      const open = fn.index + fn[0].length - 1;
      const close = matchBrace(bare, open);
      const re = /return\s*\{/g;
      re.lastIndex = open;
      for (let r = re.exec(bare); r && r.index < close; r = re.exec(bare)) {
        if (depths[r.index] === depths[open] + 1) keysAt(bare, r.index + r[0].length - 1);
      }
    }
  }
  return out;
}

/** First line in any of `files` where `name` is defined as a property/method (for reporting). */
export function findDefinition(files, name) {
  const re = new RegExp(`(?<![\\w$.])${name.replace(/\$/g, '\\$')}\\s*(?:\\(|:)`);
  for (const [file, src] of files) {
    const { bare } = scanJs(src);
    const m = re.exec(bare);
    if (m) return { file, line: lineIndex(src)(m.index) };
  }
  return null;
}
