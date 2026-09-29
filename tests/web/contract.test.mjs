// JS <-> C# drift checks that are cheaper as tests than as lint rules: settings DTO, sort enum,
// hotkey grammar. Plus unit tests of the lint's own scanners (so the gate itself is trustworthy).
import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb, readRepo, installDomStubs, csFields, camel } from './_setup.mjs';
import { scanJs, scanCs } from '../../ci/web/lib/scan.mjs';
import { parseModule } from '../../ci/web/lib/modules.mjs';
import { uiCommands, csSwitchCases, quickPageProtocol } from '../../ci/web/lib/contract.mjs';

const S = await importWeb('js/store.js');
installDomStubs();
const H = await importWeb('quick/hotkey-input.js');
const CONTRACTS = readRepo('src/GamesHub/Shared/Contracts.cs');
const SETTINGS_STORE = readRepo('src/GamesHub/Core/SettingsStore.cs');

describe('settings contract', () => {
  test('DEFAULT_SETTINGS has exactly the AppSettings fields (SettingsStore.ToDto)', () => {
    assert.deepEqual(Object.keys(S.DEFAULT_SETTINGS).sort(), csFields(CONTRACTS, 'AppSettings').sort());
  });

  test('literal C# defaults match the UI defaults', () => {
    const body = CONTRACTS.slice(CONTRACTS.indexOf('class AppSettings'));
    let checked = 0;
    for (const m of body.slice(0, body.indexOf('\n    }')).matchAll(/public\s+(bool|string)\s+(\w+)\s*=\s*("([^"]*)"|true|false)\s*;/g)) {
      const want = m[1] === 'bool' ? m[3] === 'true' : m[4];
      assert.equal(S.DEFAULT_SETTINGS[camel(m[2])], want, m[2]);
      checked++;
    }
    assert.ok(checked > 15, `only ${checked} literal defaults found: parser drift?`);
  });

  test('SERVER_SORTS equals SettingsStore.SortByValues', () => {
    const vals = /SortByValues\s*=\s*\{([^}]*)\}/.exec(SETTINGS_STORE)[1].match(/"(\w+)"/g).map((s) => s.slice(1, -1));
    assert.deepEqual(S.SERVER_SORTS, vals);
    assert.ok(vals.includes(S.DEFAULT_SETTINGS.sortBy));
  });
});

describe('hotkey grammar (web component <-> mock/SettingsStore)', () => {
  const HOTKEY_RE = new RegExp(/const HOTKEY_RE = \/(.+)\/;/.exec(readRepo('web/js/mock-features.js'))[1]);
  const CORPUS = [
    'ctrl+alt+g', 'Ctrl + Shift + Space', 'control+ALT+delete', 'win+shift+s', 'meta+e', 'alt+f4', 'F9', 'f24', 'ctrl+num5',
    'ctrl+numpad0', 'shift+pgup', 'ctrl+alt+espaço', 'Alt+PrtSc', 'ctrl+alt+up', 'ctrl+1',
    'g', 'ctrl', 'ctrl+alt', 'ctrl+ctrl+g', 'ctrl+g+h', 'ctrl++', '', 'ctrl+f25', 'ctrl+ç', 'hyper+g', null,
  ];
  test('every normalized hotkey is accepted by the mock validator and is canonical (idempotent)', () => {
    let valid = 0;
    for (const input of CORPUS) {
      const out = H.normalizeHotkey(input);
      if (out === null) continue;
      valid++;
      assert.match(out, HOTKEY_RE, `${input} -> ${out}`);
      assert.equal(H.normalizeHotkey(out), out);
    }
    assert.equal(valid, 15);
  });
  test('invalid inputs are rejected', () => {
    for (const bad of ['g', 'ctrl', 'ctrl+alt', 'ctrl+ctrl+g', 'ctrl+g+h', 'ctrl++', '', 'ctrl+f25', 'ctrl+ç', 'hyper+g', null]) {
      assert.equal(H.normalizeHotkey(bad), null, String(bad));
    }
  });
  test('modifier order and labels', () => {
    assert.equal(H.normalizeHotkey('win+shift+alt+ctrl+k'), 'Ctrl+Alt+Shift+Win+K');
    assert.deepEqual(H.hotkeyLabels('Ctrl+Shift+Space'), ['Ctrl', 'Shift', 'Espaço']);
    assert.deepEqual(H.comboFromEvent({ ctrlKey: true, altKey: true, key: 'g', code: 'KeyG' }), { mods: ['Ctrl', 'Alt'], key: 'G', text: 'Ctrl+Alt+G' });
    assert.equal(H.keyFromEvent({ key: '!', code: 'Digit1' }), '1');
    assert.equal(H.keyFromEvent({ key: 'Shift', code: 'ShiftLeft' }), '');
  });
});

describe('lint scanners', () => {
  test('scanJs blanks comments and literal contents but keeps template expressions and regexes intact', () => {
    const src = "const a = '// not a comment'; // real\nconst r = /['\"`]/g; const t = `x ${f('y')} z`; /* c */ a / 2;";
    const { code, bare } = scanJs(src);
    assert.equal(code.length, src.length);
    assert.ok(code.includes("'// not a comment'"));
    assert.ok(!code.includes('real') && !code.includes('/* c */'));
    assert.ok(!bare.includes('not a comment'));
    assert.ok(bare.includes("${f('") && !bare.includes("'y'"), 'template expression kept, its string blanked');
    assert.ok(bare.includes('a / 2'), 'division is not a regex');
    assert.equal(code.split('\n').length, src.split('\n').length);
  });

  test('scanCs handles verbatim, interpolated and comment-lookalike strings', () => {
    const src = 'var a = @"C:\\x""y"; var u = "https://x"; var s = $"{Json.Str(args, "sub")} {{ok}}"; // case "fake":\ncase "real":';
    const { code, bare } = scanCs(src);
    assert.ok(code.includes('"https://x"'), '"//" inside a string is not a comment');
    assert.ok(!code.includes('fake'));
    assert.ok(bare.includes('Json.Str(args, "   ")'), 'interpolation hole is code');
    assert.ok(code.includes('case "real":'));
  });

  test('parseModule understands every export/import form used in web/', () => {
    const m = parseModule([
      "import A, { b as c, d } from './x.js';", "import * as NS from './y.js';", "import './side.js';",
      "export { e, f as g } from './z.js';", 'export * from "./all.js";', 'export async function h() {}',
      'export function* gen() {}', 'export class K {}', 'export const { p, q: r } = obj;', 'export let v = 1;',
      'export default 42;', 'const w = 1; export { w, w as ww };', "const { m1 } = await import('./dyn.js');",
      "NS.used(); const s = 'NS.notUsed';", "// import { nope } from './comment.js';",
    ].join('\n'));
    assert.deepEqual(m.imports.map((i) => i.spec), ['./x.js', './y.js', './side.js', './z.js', './all.js', './dyn.js']);
    assert.deepEqual(m.imports[0].names.map((n) => n.imported), ['default', 'b', 'd']);
    assert.deepEqual(m.imports[5].names.map((n) => n.imported), ['m1']);
    assert.deepEqual([...m.exports].sort(), ['K', 'default', 'e', 'g', 'gen', 'h', 'p', 'r', 'v', 'w', 'ww'].sort());
    assert.deepEqual(m.starFrom, ['./all.js']);
    assert.deepEqual(m.nsUses.map((u) => u.name), ['used']);
  });

  test('uiCommands discovers wrappers and flags unverifiable dynamic names', () => {
    const files = new Map([
      ['a.js', "export async function run(name, args) {\n  try { return await bridge.call(name, args); } catch { return null; }\n}\nrun('launch', {});\nfn.call(this, 1);\n"],
      ['b.js', "const call = (name, args) => A.run(name, args);\ncall('reveal');\nA.getBridge().call('undo', {});\nbridge.call(cmdName);\n"],
    ]);
    const r = uiCommands(files);
    assert.deepEqual([...r.commands.keys()].sort(), ['launch', 'reveal', 'undo']);
    assert.deepEqual(r.dynamic.map((d) => d.arg), ['cmdName']);
    assert.ok(r.wrappers.includes('run') && r.wrappers.includes('call'));
  });

  test('csSwitchCases only reads the top level of the matching switch', () => {
    const src = 'switch (name) {\n case "a": switch (x) { case "inner": break; } return 1;\n case "b": return 2;\n}\nswitch (other) { case "c": break; }';
    assert.deepEqual(csSwitchCases(src, /switch\s*\(\s*name\s*\)/).map((c) => c.name), ['a', 'b']);
  });

  test('quick page protocol extraction', () => {
    const p = quickPageProtocol("send({ type: x ? 'reveal' : 'launch', id }); send({ type: 'hide' });\nfunction on(msg) { switch (msg.type) { case 'show': break; case 'status': break; } }");
    assert.deepEqual([...p.sends.keys()].sort(), ['hide', 'launch', 'reveal']);
    assert.deepEqual([...p.handles.keys()].sort(), ['show', 'status']);
  });
});
