#!/usr/bin/env node
// GamesHub web-UI static gate. Zero dependencies (node:* only), Node >= 20, Windows + Linux.
//
//   node ci/web/lint.mjs [--root <repoRoot>] [--quiet] [--json] [--no-baseline]
//
// Exit 0 = clean (warnings allowed), 1 = at least one error, 2 = the linter itself crashed.
// Output: "<file>:<line>: [check] message" (paths relative to the repo root), then a summary line.
// See ci/web/README.md for what each check guards.
import { readFileSync, readdirSync, statSync, existsSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { spawn } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { checkModuleGraph } from './lib/modules.mjs';
import { checkRefs, checkUrls, checkHygiene, checkBudgets, checkHtml, BUDGETS } from './lib/static-checks.mjs';
import {
  uiCommands, uiEvents, csCommands, csMatches, csSwitchCases, jsMatches, timeoutKeys, quickPageProtocol, mockCommands, staticMockCommands, findDefinition,
} from './lib/contract.mjs';

const TEXT_EXT = /\.(html|css|js|mjs|json|md|svg|txt)$/i;

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const abs = path.join(dir, name);
    const st = statSync(abs);
    if (st.isDirectory()) walk(abs, out);
    else out.push({ abs, size: st.size });
  }
  return out;
}

/** `node --check` every module (as .mjs so it parses as an ES module on Node 20/22/24), in parallel. */
async function checkParse(jsFiles, report) {
  const tmp = mkdtempSync(path.join(os.tmpdir(), 'gh-weblint-'));
  const jobs = jsFiles.map((f, i) => ({ f, copy: path.join(tmp, `m${i}.mjs`) }));
  for (const j of jobs) writeFileSync(j.copy, j.f.src);
  const run = (j) => new Promise((resolve) => {
    const child = spawn(process.execPath, ['--check', j.copy], { stdio: ['ignore', 'ignore', 'pipe'] });
    let err = '';
    child.stderr.on('data', (d) => { err += d; });
    child.on('error', (e) => { report('error', j.f.rel, 1, 'parse', `could not run node --check: ${e.message}`); resolve(); });
    child.on('close', (code) => {
      if (code !== 0) {
        const ln = Number(new RegExp(`${path.basename(j.copy).replace('.', '\\.')}:(\\d+)`).exec(err)?.[1] || 1);
        const msg = /(SyntaxError[^\n]*)/.exec(err)?.[1] || err.trim().split('\n').pop();
        report('error', j.f.rel, ln, 'parse', msg.trim());
      }
      resolve();
    });
  });
  const queue = jobs.slice();
  const width = Math.max(2, Math.min(8, os.cpus().length));
  await Promise.all(Array.from({ length: width }, async () => { while (queue.length) await run(queue.shift()); }));
  rmSync(tmp, { recursive: true, force: true });
}

async function checkContract(ctx, report, info) {
  const { root, webRoot, jsFiles } = ctx;
  const rel = (abs) => path.relative(root, abs).split(path.sep).join('/');
  const coreDir = path.join(root, 'src', 'GamesHub', 'Core');
  const csFiles = new Map();
  if (existsSync(coreDir)) {
    for (const n of readdirSync(coreDir)) if (/^BridgeCommands.*\.cs$/.test(n)) csFiles.set(path.join(coreDir, n), readFileSync(path.join(coreDir, n), 'utf8'));
  }
  if (!csFiles.size) { report('error', 'src/GamesHub/Core', 0, 'bridge', 'BridgeCommands*.cs not found: cannot cross-check commands'); return; }
  const cs = csCommands(csFiles);
  if (!cs.size) { report('error', rel([...csFiles.keys()][0]), 1, 'bridge', 'no `case "x":` found in `switch (name)`: dispatch format changed?'); return; }

  const isMock = (f) => /[\\/]js[\\/]mock[\w-]*\.js$/.test(f.abs);
  const uiFiles = new Map(jsFiles.filter((f) => f.abs.includes(`${path.sep}js${path.sep}`) && !isMock(f)).map((f) => [f.abs, f.src]));
  const mockFiles = new Map(jsFiles.filter(isMock).map((f) => [f.abs, f.src]));
  const ui = uiCommands(uiFiles);
  let mock = null;
  try { mock = await mockCommands(path.join(webRoot, 'js', 'mock.js')); }
  catch (err) { report('error', 'web/js/mock.js', 1, 'bridge', `could not load the mock in Node (demo mode is broken): ${err.message.split('\n')[0]}`); }
  const staticMock = staticMockCommands(mockFiles);
  if (mock) {
    const diff = [...mock].filter((n) => !staticMock.has(n)).concat([...staticMock].filter((n) => !mock.has(n)));
    if (diff.length) report('warn', 'web/js/mock.js', 1, 'bridge', `static mock-command extraction disagrees with runtime (${diff.join(', ')}): update staticMockCommands()`);
  } else {
    mock = staticMock; // keep cross-checking with the static view
  }

  for (const d of ui.dynamic) report('error', rel(d.file), d.line, 'bridge', `bridge command name is not a string literal (${d.arg}): cannot be verified`);
  for (const [name, sites] of ui.commands) {
    const s = sites[0];
    if (!cs.has(name)) report('error', rel(s.file), s.line, 'bridge', `UI sends "${name}" but the C# bridge has no case "${name}" (BridgeCommands*.cs)`);
    if (mock && !mock.has(name)) report('error', rel(s.file), s.line, 'bridge', `UI sends "${name}" but the demo mock does not implement it`);
  }
  if (mock) {
    for (const [name, at] of cs) if (!mock.has(name)) report('error', rel(at.file), at.line, 'bridge', `C# command "${name}" is not implemented in the mock (demo mode would fail)`);
    for (const name of mock) {
      if (cs.has(name)) continue;
      const at = findDefinition(mockFiles, name);
      report('error', at ? rel(at.file) : 'web/js/mock.js', at?.line || 1, 'bridge', `mock implements "${name}" but the C# bridge does not`);
    }
  }
  const bridgeJs = jsFiles.find((f) => f.abs === path.join(webRoot, 'js', 'bridge.js'));
  if (bridgeJs) {
    for (const [k, at] of timeoutKeys(bridgeJs.src)) if (!cs.has(k)) report('error', bridgeJs.rel, at.line, 'bridge', `TIMEOUTS has "${k}", which is not a C# command (typo?)`);
  }

  // events C# -> JS
  const srcDir = path.join(root, 'src', 'GamesHub');
  const allCs = new Map(walk(srcDir).filter((f) => f.abs.endsWith('.cs')).map((f) => [f.abs, readFileSync(f.abs, 'utf8')]));
  const emitted = csMatches(allCs, /\bEmit\(\s*"(\w+)"/);
  const subscribed = uiEvents(uiFiles);
  for (const [name, sites] of subscribed) {
    if (!emitted.has(name)) report('error', rel(sites[0].file), sites[0].line, 'bridge', `UI listens to event "${name}" but C# never emits it`);
  }
  for (const [name, at] of jsMatches(mockFiles, /(?<![\w$.])event\(\s*(['"])(\w+)\1/)) {
    if (!emitted.has(name)) report('error', rel(at.file), at.line, 'bridge', `mock emits event "${name}" that C# never emits`);
  }
  for (const [name, at] of emitted) {
    if (!subscribed.has(name)) report('warn', rel(at.file), at.line, 'bridge', `C# emits event "${name}" but the UI never listens to it`);
  }

  // quick-launch page protocol (web/quick/quick.js <-> QuickLaunch/*.cs)
  const quickJs = jsFiles.find((f) => f.abs === path.join(webRoot, 'quick', 'quick.js'));
  const qDir = path.join(srcDir, 'QuickLaunch');
  let quickPairs = 0;
  if (quickJs && existsSync(qDir)) {
    const qCs = new Map(walk(qDir).filter((f) => f.abs.endsWith('.cs')).map((f) => [f.abs, readFileSync(f.abs, 'utf8')]));
    const handled = new Set();
    for (const [, src] of qCs) for (const c of csSwitchCases(src, /switch\s*\(\s*Json\.Str\(\s*msg\s*,\s*"type"\s*\)\s*\)/)) handled.add(c.name);
    const posted = csMatches(qCs, /\btype\s*=\s*"(\w+)"/);
    const page = quickPageProtocol(quickJs.src);
    for (const [t, at] of page.sends) if (!handled.has(t)) report('error', quickJs.rel, at.line, 'bridge', `quick page sends "${t}" but QuickLaunchController does not handle it`);
    for (const [t, at] of posted) if (!page.handles.has(t)) report('error', rel(at.file), at.line, 'bridge', `C# posts "${t}" to the quick page but quick.js has no case for it`);
    quickPairs = page.sends.size + posted.size;
  }

  const unused = [...cs.keys()].filter((n) => !ui.commands.has(n));
  info.push(`bridge: ${ui.commands.size} UI commands, ${cs.size} C# commands, ${mock ? mock.size : '?'} mock commands, `
    + `${subscribed.size} events, ${quickPairs} quick-launch messages; wrappers: ${ui.wrappers.join(', ')}`);
  if (unused.length) info.push(`bridge: C# commands not used by the UI (ok, informative): ${unused.join(', ')}`);
}

export const BASELINE_FILE = path.join(path.dirname(fileURLToPath(import.meta.url)), 'baseline.json');

/**
 * Known, tracked issues in code this gate does not own. Each entry { file, check, contains, reason }
 * downgrades matching errors to warnings. Entries that no longer match are reported (warning) so the
 * baseline only shrinks.
 */
function applyBaseline(problems, baseline, info) {
  const used = new Set();
  for (const p of problems) {
    if (p.level !== 'error') continue;
    const i = baseline.findIndex((b) => b.file === p.file && b.check === p.check && p.msg.includes(b.contains));
    if (i < 0) continue;
    used.add(i);
    p.level = 'warn';
    p.msg += ` (baselined: ${baseline[i].reason})`;
  }
  baseline.forEach((b, i) => {
    if (!used.has(i)) problems.push({ level: 'warn', file: 'ci/web/baseline.json', line: 1, check: 'baseline', msg: `stale entry (fixed?) — remove: ${b.file} [${b.check}] "${b.contains}"` });
  });
  if (baseline.length) info.push(`baseline: ${used.size}/${baseline.length} known issue(s) downgraded to warnings (ci/web/baseline.json)`);
}

function loadBaseline(file) {
  if (!file || !existsSync(file)) return [];
  const data = JSON.parse(readFileSync(file, 'utf8'));
  return Array.isArray(data.known) ? data.known : [];
}

export async function runLint({ root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..'), baselineFile = BASELINE_FILE } = {}) {
  const baseline = loadBaseline(baselineFile);
  const t0 = performance.now();
  root = path.resolve(root);
  const webRoot = path.join(root, 'web');
  const problems = [];
  const info = [];
  const report = (level, file, line, check, msg) => problems.push({ level, file, line, check, msg });
  if (!existsSync(webRoot)) {
    report('error', 'web', 0, 'setup', `no web/ folder under ${root}`);
    return { problems, info, ms: 0, stats: {} };
  }
  const all = walk(webRoot).map((f) => ({
    ...f, rel: path.relative(root, f.abs).split(path.sep).join('/'),
    src: TEXT_EXT.test(f.abs) ? readFileSync(f.abs, 'utf8') : null,
  }));
  const text = all.filter((f) => f.src !== null);
  const jsFiles = text.filter((f) => /\.m?js$/.test(f.abs));
  const sources = new Map(jsFiles.map((f) => [f.abs, f.src]));
  const timings = {};
  const time = async (name, fn) => { const t = performance.now(); const r = await fn(); timings[name] = Math.round(performance.now() - t); return r; };

  await time('parse', () => checkParse(jsFiles, report));
  const graph = await time('imports', () => checkModuleGraph(jsFiles.map((f) => f.abs), webRoot, sources,
    (abs, line, msg) => report('error', path.relative(root, abs).split(path.sep).join('/'), line, 'imports', msg)));
  const refs = await time('refs', () => checkRefs(text, webRoot, report));
  const urls = await time('urls', () => checkUrls(text, report));
  await time('hygiene', () => checkHygiene(text, report));
  const budget = await time('budget', () => checkBudgets(all, report));
  await time('html', () => checkHtml(text, report));
  await time('bridge', () => checkContract({ root, webRoot, jsFiles }, report, info));

  if (baseline) applyBaseline(problems, baseline, info);
  info.unshift(`files: ${all.length} (${jsFiles.length} JS modules, ${graph.edges} imports, ${refs} local refs, ${urls} URLs)`);
  info.push(`size: web/ = ${(budget.total / 1024).toFixed(1)} KB of ${(BUDGETS.totalBytes / 1024).toFixed(0)} KB; largest: `
    + budget.largest.map((f) => `${f.rel.replace(/^web\//, '')} ${(f.size / 1024).toFixed(1)} KB`).join(', '));
  if (budget.near.length) info.push(`near line budget (>90%): ${budget.near.map((f) => `${f.rel.replace(/^web\//, '')} ${f.lines}`).join(', ')}`);
  info.push(`timings (ms): ${Object.entries(timings).map(([k, v]) => `${k} ${v}`).join(', ')}`);
  return { problems, info, ms: Math.round(performance.now() - t0) };
}

async function main() {
  const args = process.argv.slice(2);
  const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
  const res = await runLint({
    ...(opt('--root') ? { root: opt('--root') } : {}),
    ...(args.includes('--no-baseline') ? { baselineFile: null } : {}),
  });
  const errors = res.problems.filter((p) => p.level === 'error');
  const warns = res.problems.filter((p) => p.level === 'warn');
  if (args.includes('--json')) {
    process.stdout.write(`${JSON.stringify(res, null, 2)}\n`);
  } else {
    const order = (a, b) => a.file.localeCompare(b.file) || a.line - b.line;
    for (const p of [...errors].sort(order)) console.log(`${p.file}:${p.line}: [${p.check}] ${p.msg}`);
    for (const p of [...warns].sort(order)) console.log(`${p.file}:${p.line}: warning: [${p.check}] ${p.msg}`);
    if (!args.includes('--quiet')) for (const line of res.info) console.log(`  ${line}`);
    console.log(`web lint: ${errors.length ? 'FAILED' : 'OK'} — ${errors.length} error(s), ${warns.length} warning(s) in ${res.ms} ms`);
  }
  process.exitCode = errors.length ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch((err) => { console.error(`web lint crashed: ${err.stack || err}`); process.exitCode = 2; });
}
