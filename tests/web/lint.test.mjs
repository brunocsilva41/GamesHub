// The gate must pass on the repo and must FAIL on a deliberately broken copy (made in the OS temp dir,
// never in the repo). Every check gets at least one injected fault.
import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { cpSync, mkdtempSync, readFileSync, writeFileSync, rmSync, mkdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { ROOT } from './_setup.mjs';
import { runLint } from '../../ci/web/lint.mjs';

const LINT = path.join(ROOT, 'ci', 'web', 'lint.mjs');
const errorsOf = (res) => res.problems.filter((p) => p.level === 'error');

function copyRepo() {
  const tmp = mkdtempSync(path.join(os.tmpdir(), 'gh-weblint-test-'));
  cpSync(path.join(ROOT, 'web'), path.join(tmp, 'web'), { recursive: true });
  for (const dir of ['Core', 'QuickLaunch']) {
    mkdirSync(path.join(tmp, 'src', 'GamesHub', dir), { recursive: true });
    cpSync(path.join(ROOT, 'src', 'GamesHub', dir), path.join(tmp, 'src', 'GamesHub', dir), { recursive: true });
  }
  return tmp;
}

const edit = (root, rel, fn) => {
  const p = path.join(root, rel);
  writeFileSync(p, fn(readFileSync(p, 'utf8')));
};

describe('lint on the real repo', () => {
  test('passes (0 errors) with the checked-in baseline', async () => {
    const res = await runLint({ root: ROOT });
    assert.deepEqual(errorsOf(res).map((p) => `${p.file}:${p.line}: [${p.check}] ${p.msg}`), []);
  });
  test('baseline is minimal: without it only the baselined issues fail', async () => {
    const baseline = JSON.parse(readFileSync(path.join(ROOT, 'ci', 'web', 'baseline.json'), 'utf8')).known;
    const res = await runLint({ root: ROOT, baselineFile: null });
    assert.equal(errorsOf(res).length, baseline.length);
  });
});

describe('lint on a deliberately broken copy', () => {
  let tmp;
  let res;
  before(async () => {
    tmp = copyRepo();
    edit(tmp, 'web/js/store.js', (s) => `import { nope } from './does-not-exist.js';\n${s}`);
    edit(tmp, 'web/js/util.js', (s) => `${s}\nimport { notExported } from './emitter.js';\nexport const ping = () => fetch('http://tracker.example.com/p');\n`);
    edit(tmp, 'web/js/actions.js', (s) => `${s}\nexport const rocket = () => run('launchRocket', {});\n`);
    edit(tmp, 'web/js/actions2.js', (s) => `${s}\nexport const z = () => A.doesNotExist();\nexport const dyn = (bridge, n) => bridge.call(n);\n`);
    edit(tmp, 'web/js/main.js', (s) => `${s}\nexport const ghost = (bridge) => bridge.on('ghostEvent', () => {});\n`);
    edit(tmp, 'web/js/emitter.js', (s) => `${s}\nconsole.log('x');\ndebugger;\n// TODO fix later\n`);
    edit(tmp, 'web/js/views/setui.js', (s) => `${s}\nconst = ;\n`);
    edit(tmp, 'web/js/views/states.js', (s) => `${s}${'// padding\n'.repeat(300)}`);
    edit(tmp, 'web/css/base.css', (s) => `${s}\n.x { background: url('../img/missing.png'); }\n`);
    edit(tmp, 'web/index.html', (s) => s.replace('</head>',
      '<script src="https://cdn.jsdelivr.net/npm/x.js"></script>\n<link rel="stylesheet" href="css/nope.css">\n</head>')
      .replace('<noscript>', '<img src="logo.png">\n<button class="x"></button>\n<div onclick="go()">x</div>\n<noscript>'));
    edit(tmp, 'web/quick/index.html', (s) => s.replace('lang="pt-BR"', 'lang="en"'));
    edit(tmp, 'web/quick/quick.js', (s) => `${s}\nsend({ type: 'teleport' });\n`);
    edit(tmp, 'src/GamesHub/Core/BridgeCommands.cs', (s) => s.replace('case "quit":', 'case "newThing": return Done(BridgeResult.Success(Empty()));\n                case "quit":'));
    res = await runLint({ root: tmp, baselineFile: null });
  });
  after(() => { if (tmp) rmSync(tmp, { recursive: true, force: true }); });

  const EXPECT = [
    ['imports', 'web/js/store.js', /"\.\/does-not-exist\.js" does not resolve/],
    ['imports', 'web/js/util.js', /"notExported" is not exported by \.\/emitter\.js/],
    ['imports', 'web/js/actions2.js', /A\.doesNotExist/],
    ['bridge', 'web/js/actions.js', /UI sends "launchRocket" but the C# bridge has no case/],
    ['bridge', 'web/js/actions.js', /UI sends "launchRocket" but the demo mock does not implement it/],
    ['bridge', 'web/js/actions2.js', /not a string literal \(n\)/],
    ['bridge', 'web/js/main.js', /event "ghostEvent" but C# never emits it/],
    ['bridge', 'src/GamesHub/Core/BridgeCommands.cs', /C# command "newThing" is not implemented in the mock/],
    ['bridge', 'web/quick/quick.js', /sends "teleport"/],
    // plain substrings (not regexes): these are URLs quoted inside the message, not URL validators
    ['urls', 'web/js/util.js', 'http://tracker.example.com'],
    ['urls', 'web/index.html', 'cdn.jsdelivr.net'],
    ['refs', 'web/index.html', /"https:\/\/cdn\.jsdelivr\.net\/npm\/x\.js" is not local/],
    ['refs', 'web/index.html', /"css\/nope\.css" does not exist/],
    ['refs', 'web/css/base.css', /"\.\.\/img\/missing\.png" does not exist/],
    ['hygiene', 'web/js/emitter.js', /console\.log/],
    ['hygiene', 'web/js/emitter.js', /debugger/],
    ['hygiene', 'web/js/emitter.js', /TODO without an issue reference/],
    ['parse', 'web/js/views/setui.js', /SyntaxError/],
    ['budget', 'web/js/views/states.js', /lines > 300/],
    ['budget', 'web/quick/quick.js', /lines > 300/],
    ['html', 'web/index.html', /<img> without alt/],
    ['html', 'web/index.html', /<button> without text or aria-label/],
    ['html', 'web/index.html', /inline event handler onclick=/],
    ['html', 'web/quick/index.html', /lang="pt-BR"/],
  ];
  for (const [check, file, want] of EXPECT) {
    const matches = typeof want === 'string' ? (msg) => msg.includes(want) : (msg) => want.test(msg);
    const label = typeof want === 'string' ? want : want.source;
    test(`catches [${check}] ${file} ${label.slice(0, 50)}`, () => {
      const hit = errorsOf(res).find((p) => p.check === check && p.file === file && matches(p.msg));
      assert.ok(hit, `not reported. Errors were:\n${errorsOf(res).map((p) => `  ${p.file}:${p.line}: [${p.check}] ${p.msg}`).join('\n')}`);
      assert.ok(hit.line >= 1 || file.startsWith('src/'), 'has a line number');
    });
  }

  test('reports the syntax error on the right line', () => {
    const lines = readFileSync(path.join(tmp, 'web/js/views/setui.js'), 'utf8').split('\n');
    const hit = errorsOf(res).find((p) => p.check === 'parse');
    assert.match(lines[hit.line - 1], /const = ;/);
  });

  test('no false positives: nothing reported in untouched files', () => {
    const touched = new Set(EXPECT.map(([, f]) => f));
    const baselined = new Set(JSON.parse(readFileSync(path.join(ROOT, 'ci', 'web', 'baseline.json'), 'utf8')).known.map((b) => b.file));
    const stray = errorsOf(res).filter((p) => !touched.has(p.file) && !baselined.has(p.file)
      // the broken store.js import legitimately makes the mock unloadable (the lint then falls back to a static view)
      && !(p.file === 'web/js/mock.js' && /could not load the mock/.test(p.msg)));
    assert.deepEqual(stray.map((p) => `${p.file}:${p.line}: [${p.check}] ${p.msg}`), []);
  });

  test('CLI exits 1 and prints "file:line: [check] message" + a summary', () => {
    const r = spawnSync(process.execPath, [LINT, '--root', tmp, '--no-baseline', '--quiet'], { encoding: 'utf8' });
    assert.equal(r.status, 1, r.stderr);
    assert.match(r.stdout, /^web\/js\/store\.js:1: \[imports\] /m);
    assert.match(r.stdout, /^web lint: FAILED — \d+ error\(s\)/m);
  });
});
