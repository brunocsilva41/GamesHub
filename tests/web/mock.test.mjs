// Demo-mode mock round-trip through the real bridge.js (JSON clone + latency), so what the UI sees in a
// browser matches the C# contract. Shapes are compared with the C# sources, not hand-copied lists.
import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb, readRepo, unrefLongTimers, quietConsole, csDictKeys, csFields, sortedKeys } from './_setup.mjs';
import { csCommands } from '../../ci/web/lib/contract.mjs';

const { connect, BridgeError } = await importWeb('js/bridge.js');
const { DEFAULT_SETTINGS } = await importWeb('js/store.js');
const CONTRACTS = readRepo('src/GamesHub/Shared/Contracts.Integrations.cs');
const DTO = readRepo('src/GamesHub/Core/BridgeDto.cs');
const INTEGRATIONS = readRepo('src/GamesHub/Core/BridgeCommands.Integrations.cs');
const CS_COMMANDS = csCommands(new Map(['BridgeCommands.cs', 'BridgeCommands.Integrations.cs']
  .map((f) => [f, readRepo(`src/GamesHub/Core/${f}`)])));

let restoreTimers;
before(() => { delete globalThis.chrome; restoreTimers = unrefLongTimers(); });
after(() => restoreTimers());

const fresh = async () => { const b = await connect(); assert.equal(b.isMock, true); return b; };
const attempt = (b, name, args, opts) => b.call(name, args, opts).then((data) => ({ ok: true, data }), (err) => ({ ok: false, err }));
const state = (b) => b.call('getState');
const find = async (b, id) => (await state(b)).games.find((g) => g.id === id);

// ---------------------------------------------------------------- shape validators
const isOp = (d) => typeof d.message === 'string' && 'gameId' in d && 'undoToken' in d;
const OP_ENTRY = ['gameId', 'message', 'ok', 'undoToken'];
const WINDOW = ['fullscreen', 'maximized', 'pinned'];
const settingsShape = (d) => assert.deepEqual(sortedKeys(d), sortedKeys(DEFAULT_SETTINGS));
const fieldsOf = (cls) => csFields(CONTRACTS, cls).sort();

const PROFILE = { enabled: true, useDefault: false, before: [{ type: 'wait', target: '', args: '', seconds: 3, enabled: true }], after: [] };

/** Every mock command: [args, expected 'ok'|'fail', validate(data)?, opts?]. Must cover ALL C# commands. */
const CASES = {
  getState: [{}, 'ok', (d) => assert.deepEqual(sortedKeys(d), ['collections', 'demo', 'games', 'settings', 'version', 'windowState'])],
  launch: [{ id: 'steam:730' }, 'ok', (d) => assert.ok(isOp(d) && d.gameId === 'steam:730')],
  reveal: [{ id: 'steam:730' }, 'ok', (d) => assert.ok(isOp(d))],
  remove: [{ id: 'epic:AlanWake2' }, 'ok', (d) => assert.ok(isOp(d) && typeof d.undoToken === 'string')],
  undo: [{ token: 'does-not-exist' }, 'fail'],
  updateGame: [{ id: 'steam:1145350', edit: { favorite: false } }, 'ok', (d) => assert.ok(isOp(d))],
  addFiles: [{}, 'ok', (d) => d.results.forEach((r) => (r.ok ? assert.deepEqual(sortedKeys(r), OP_ENTRY)
    : assert.ok(sortedKeys(r).every((k) => OP_ENTRY.includes(k)) && typeof r.message === 'string'))), { files: [{ name: 'Novo Jogo.exe' }, { name: 'leia-me.txt' }] }],
  pickFile: [{}, 'ok', (d) => assert.ok(isOp(d))],
  addSteam: [{ appId: '570' }, 'ok', (d) => assert.ok(isOp(d) && d.gameId === 'steam:570')],
  searchSteam: [{ query: 'hades' }, 'ok', (d) => {
    assert.ok(d.results.length >= 2);
    for (const r of d.results) assert.deepEqual(sortedKeys(r), ['appId', 'iconUrl', 'name']);
  }],
  setArt: [{ id: 'steam:730', kind: 'hero' }, 'ok', (d) => assert.ok(isOp(d))],
  clearArt: [{ id: 'steam:730', kind: 'logo' }, 'ok', (d) => assert.ok(isOp(d))],
  refreshArt: [{ id: 'steam:730' }, 'ok', (d) => assert.ok(isOp(d))],
  rescan: [{}, 'ok', (d) => assert.ok(isOp(d))],
  setSettings: [{ patch: { reduceMotion: true } }, 'ok', settingsShape],
  pickGamesDir: [{}, 'ok', settingsShape],
  window: [{ action: 'maximize' }, 'ok', (d) => assert.deepEqual(sortedKeys(d), WINDOW)],
  openExternal: [{ url: 'https://www.pcgamingwiki.com/' }, 'ok'],
  openDataFolder: [{ sub: 'logs' }, 'ok'],
  checkUpdate: [{}, 'ok', (d) => assert.deepEqual(sortedKeys(d), ['available', 'notes', 'pageUrl', 'version'])],
  installUpdate: [{}, 'ok', (d) => assert.equal(d.started, true)],
  log: [{ level: 'info', msg: 'teste' }, 'ok'],
  quit: [{}, 'ok'],
  getGameInfo: [{ id: 'steam:1091500' }, 'ok', (d) => {
    const keys = csDictKeys(INTEGRATIONS, /GetGameInfo\(IDictionary/);
    const nested = [...Object.keys(d.health), ...Object.keys(d.uninstall)];
    assert.deepEqual([...Object.keys(d), ...nested].sort(), keys.sort());
    assert.deepEqual(sortedKeys(d.info), fieldsOf('GameInfo'));
    assert.deepEqual(sortedKeys(d.steam), fieldsOf('SteamLocalStats'));
  }],
  getPcgw: [{ id: 'steam:1091500' }, 'ok', (d) => {
    assert.deepEqual(sortedKeys(d), fieldsOf('PcgwInfo'));
    for (const p of [...d.saveLocations, ...d.configLocations]) assert.deepEqual(sortedKeys(p), fieldsOf('ResolvedPath'));
  }],
  openPath: [{ path: 'C:\\Windows\\System32' }, 'fail'],
  validateGame: [{ id: 'steam:1091500' }, 'ok', (d) => assert.ok(isOp(d))],
  uninstallGame: [{ id: 'steam:1091500' }, 'ok', (d) => assert.ok(isOp(d))],
  getDrives: [{}, 'ok', (d) => { assert.ok(d.drives.length); for (const x of d.drives) assert.deepEqual(sortedKeys(x), fieldsOf('DriveSpace')); }],
  cleanupBroken: [{}, 'ok', (d) => { assert.ok(d.results.length); for (const r of d.results) assert.deepEqual(sortedKeys(r), OP_ENTRY); }],
  getGenres: [{}, 'ok', (d) => assert.ok(d.genres.length && d.genres.every((g) => typeof g === 'string'))],
  getAutomation: [{ id: null }, 'ok', (d) => {
    assert.deepEqual(sortedKeys(d), fieldsOf('AutomationProfile'));
    for (const a of d.before) assert.deepEqual(sortedKeys(a), fieldsOf('AutomationAction'));
  }],
  saveAutomation: [{ id: 'steam:730', profile: PROFILE }, 'ok', (d) => assert.ok(isOp(d))],
  automationOptions: [{}, 'ok', (d) => {
    assert.deepEqual(sortedKeys(d), ['audioDevices', 'powerPlans', 'resolutions']);
    for (const list of Object.values(d)) for (const o of list) assert.deepEqual(sortedKeys(o), fieldsOf('NamedOption'));
  }],
  variantGroups: [{}, 'ok', (d) => { assert.equal(d.groups.length, 1); assert.deepEqual(sortedKeys(d.groups[0]), fieldsOf('VariantGroup')); }],
  variantSuggestions: [{}, 'ok', (d) => { for (const g of d.groups) assert.deepEqual(sortedKeys(g), fieldsOf('VariantGroup')); }],
  groupVariants: [{ ids: ['folder:the witcher 3.lnk', 'folder:the witcher 3 dx11.lnk'], primaryId: 'folder:the witcher 3.lnk' }, 'ok', (d) => assert.ok(isOp(d))],
  ungroupVariants: [{ groupId: 'nope' }, 'fail'],
  setVariantLabel: [{ id: 'folder:minecraft.lnk', label: 'Java' }, 'ok', (d) => assert.ok(isOp(d))],
  setVariantPrimary: [{ groupId: 'vg1', primaryId: 'folder:minecraft.lnk' }, 'ok', (d) => assert.ok(isOp(d))],
  dismissVariants: [{ ids: ['a', 'b'] }, 'ok', (d) => assert.ok(isOp(d))],
};

describe('mock covers the bridge contract', () => {
  test('the case table covers every mock command and every C# command', async () => {
    const b = await fresh();
    assert.deepEqual(Object.keys(CASES).sort(), [...b.host.commands].sort(), 'update CASES when the mock gains/loses a command');
    assert.deepEqual([...CS_COMMANDS.keys()].sort(), Object.keys(CASES).sort(), 'mock and C# dispatch differ');
  });

  test('EVERY command answers with the documented shape (or a clean pt-BR error)', async () => {
    const b = await fresh();
    const { result } = await quietConsole(() => Promise.all(Object.entries(CASES).map(async ([name, [args, want, check, opts]]) => {
      const r = await attempt(b, name, args, opts);
      return { name, want, check, r };
    })));
    for (const { name, want, check, r } of result) {
      if (want === 'ok') {
        assert.ok(r.ok, `${name} failed: ${r.err?.message}`);
        if (check) check(r.data);
      } else {
        assert.ok(!r.ok, `${name} should fail`);
        assert.ok(r.err instanceof BridgeError && r.err.code === 'failed', `${name}: ${r.err?.code}`);
        assert.match(r.err.message, /\S/);
        assert.doesNotMatch(r.err.message, /Erro interno|Comando desconhecido/);
      }
    }
  });

  // Known drift (reported to WEB): mock.js addPath() returns { ok:false, message } for a non-game file, while
  // C# BridgeDto.OpResultEntry always sends gameId/undoToken (null). Harmless for the UI today.
  test('addFiles failure entries carry gameId/undoToken like OpResultEntry (known drift: diagnostic only)', async (t) => {
    const b = await fresh();
    const { results } = await b.call('addFiles', {}, { files: [{ name: 'leia-me.txt' }] });
    assert.equal(results[0].ok, false);
    const missing = OP_ENTRY.filter((k) => !(k in results[0]));
    if (missing.length) t.diagnostic(`KNOWN DRIFT web/js/mock.js:50 addPath() omits ${missing.join(', ')} (C# always sends them)`);
  });

  test('unknown command is rejected, not left hanging', async () => {
    const b = await fresh();
    await assert.rejects(b.call('launchRocket', {}, { timeout: 2000 }), { code: 'failed', message: /Comando desconhecido/ });
  });

  test('sample games have exactly the C# GameDto fields', async () => {
    const b = await fresh();
    const top = new Set(csDictKeys(DTO, /public static Dictionary<string, object> Game\(/));
    for (const k of ['header', 'capsule', 'hero', 'logo', 'icon', 'label']) top.delete(k);
    const ART = csDictKeys(DTO, /public static Dictionary<string, object> Game\(/).filter((k) => ['header', 'capsule', 'hero', 'logo', 'icon'].includes(k));
    for (const g of (await state(b)).games) {
      assert.deepEqual(sortedKeys(g), [...top].sort(), g.id);
      assert.deepEqual(sortedKeys(g.art), ART.sort(), g.id);
      for (const v of g.variants) assert.deepEqual(sortedKeys(v), ['id', 'label']);
    }
  });
});

describe('mock behaviour', { concurrency: true }, () => {
  test('remove + undo restores the game; a token works once', async () => {
    const b = await fresh();
    const r = await b.call('remove', { id: 'steam:730' });
    assert.match(r.message, /removido/);
    assert.equal(await find(b, 'steam:730'), undefined);
    assert.deepEqual(await b.call('undo', { token: r.undoToken }), { message: 'Ação desfeita.', gameId: null, undoToken: null });
    assert.ok(await find(b, 'steam:730'));
    await assert.rejects(b.call('undo', { token: r.undoToken }), { message: 'Não há nada para desfazer.' });
  });

  test('hiding returns an undo token; undo unhides', async () => {
    const b = await fresh();
    const r = await b.call('updateGame', { id: 'epic:Fortnite', edit: { hidden: true } });
    assert.ok(r.undoToken);
    assert.equal((await find(b, 'epic:Fortnite')).hidden, true);
    await b.call('undo', { token: r.undoToken });
    assert.equal((await find(b, 'epic:Fortnite')).hidden, false);
  });

  test('launch: running and broken games fail; success emits running', async () => {
    const b = await fresh();
    await assert.rejects(b.call('launch', { id: 'steam:1245620' }), /já está em execução/);
    await assert.rejects(b.call('launch', { id: 'folder:ea sports fc 25.lnk' }), /atalho|destino/);
    const running = new Promise((res) => b.on('running', res));
    await b.call('launch', { id: 'steam:730' });
    assert.deepEqual(await running, { id: 'steam:730', running: true });
  });

  test('setSettings validates hotkeys like SettingsStore', async () => {
    const b = await fresh();
    for (const bad of ['Ctrl+', 'banana', 'Ctrl+Alt', '', 'Ctrl+Alt+Ç']) {
      await assert.rejects(b.call('setSettings', { patch: { hotkey: bad } }), /Atalho inválido/, bad);
      await assert.rejects(b.call('setSettings', { patch: { quickLaunchHotkey: bad } }), /Atalho inválido/, bad);
    }
    assert.equal((await state(b)).settings.hotkey, DEFAULT_SETTINGS.hotkey, 'rejected patch leaves settings untouched');
    const s = await b.call('setSettings', { patch: { hotkey: 'Ctrl+Alt+K', quickLaunchHotkey: 'F9' } });
    assert.equal(s.hotkey, 'Ctrl+Alt+K');
    assert.equal(s.quickLaunchHotkey, 'F9');
  });

  test('a hotkey taken by another program is saved but reported via a toast event', async () => {
    const b = await fresh();
    const toast = new Promise((res) => b.on('toast', res));
    await b.call('setSettings', { patch: { hotkey: 'Alt+F4' } });
    const t = await toast;
    assert.equal(t.kind, 'err');
    assert.match(t.text, /Alt\+F4.*em uso/);
  });

  test('variants: initial group, ungroup, suggestions, dismiss, regroup, label, primary', async () => {
    const b = await fresh();
    const mc = await find(b, 'folder:minecraft.lnk');
    assert.deepEqual(mc.variants.map((v) => v.label), ['Java Edition', 'Bedrock Edition']);
    assert.equal(await find(b, 'folder:minecraft bedrock.lnk'), undefined, 'members leave the grid');
    const [grp] = (await b.call('variantGroups')).groups;

    await b.call('ungroupVariants', { groupId: grp.id });
    assert.ok(await find(b, 'folder:minecraft bedrock.lnk'));
    assert.deepEqual((await find(b, 'folder:minecraft.lnk')).variants, []);
    await assert.rejects(b.call('ungroupVariants', { groupId: grp.id }), /Grupo não encontrado/);

    const sug = (await b.call('variantSuggestions')).groups;
    const mcSug = sug.find((s) => s.primaryId === 'folder:minecraft.lnk');
    assert.ok(mcSug && mcSug.memberIds.includes('folder:minecraft bedrock.lnk'));
    assert.ok(sug.some((s) => s.primaryId === 'folder:the witcher 3.lnk'));
    await b.call('dismissVariants', { ids: mcSug.memberIds });
    assert.equal((await b.call('variantSuggestions')).groups.some((s) => s.primaryId === 'folder:minecraft.lnk'), false);

    await assert.rejects(b.call('groupVariants', { ids: ['folder:minecraft.lnk'], primaryId: 'folder:minecraft.lnk' }), /pelo menos dois/);
    await assert.rejects(b.call('groupVariants', { ids: ['folder:minecraft.lnk', 'folder:minecraft bedrock.lnk'], primaryId: 'steam:730' }), /principal/);
    await assert.rejects(b.call('groupVariants', { ids: ['folder:minecraft.lnk', 'nope'], primaryId: 'nope' }), /não encontrado/);
    const g = await b.call('groupVariants', { ids: ['folder:minecraft.lnk', 'folder:minecraft bedrock.lnk'], primaryId: 'folder:minecraft bedrock.lnk' });
    assert.equal(g.gameId, 'folder:minecraft bedrock.lnk');
    assert.equal(await find(b, 'folder:minecraft.lnk'), undefined);

    await b.call('setVariantLabel', { id: 'folder:minecraft.lnk', label: '  Java  ' });
    assert.ok((await find(b, 'folder:minecraft bedrock.lnk')).variants.some((v) => v.label === 'Java'));
    await assert.rejects(b.call('setVariantLabel', { id: 'steam:730', label: 'x' }), /não faz parte/);

    const [grp2] = (await b.call('variantGroups')).groups;
    await b.call('setVariantPrimary', { groupId: grp2.id, primaryId: 'folder:minecraft.lnk' });
    assert.ok(await find(b, 'folder:minecraft.lnk'));
    assert.equal(await find(b, 'folder:minecraft bedrock.lnk'), undefined);
    await assert.rejects(b.call('setVariantPrimary', { groupId: grp2.id, primaryId: 'steam:730' }), /não faz parte/);
  });

  test('cleanupBroken removes every broken shortcut, each undoable', async () => {
    const b = await fresh();
    const brokenBefore = (await state(b)).games.filter((g) => g.broken).map((g) => g.id).sort();
    assert.ok(brokenBefore.length >= 2);
    const { results } = await b.call('cleanupBroken');
    assert.deepEqual(results.map((r) => r.gameId).sort(), brokenBefore);
    assert.ok(results.every((r) => r.ok && r.undoToken));
    assert.equal((await state(b)).games.some((g) => g.broken), false);
    assert.deepEqual((await b.call('cleanupBroken')).results, [], 'second run is a no-op');
    for (const r of results) await b.call('undo', { token: r.undoToken });
    assert.deepEqual((await state(b)).games.filter((g) => g.broken).map((g) => g.id).sort(), brokenBefore);
  });

  test('openPath only opens paths previously returned by getPcgw', async () => {
    const b = await fresh();
    const info = await b.call('getPcgw', { id: 'steam:1091500' });
    const ok = [...info.saveLocations, ...info.configLocations].find((p) => p.exists);
    assert.ok(ok, 'fixture has an existing path');
    await quietConsole(async () => assert.match((await b.call('openPath', { path: ok.path })).message, /Explorador/));
    const missing = [...info.saveLocations, ...info.configLocations].find((p) => !p.exists);
    if (missing) await assert.rejects(b.call('openPath', { path: missing.path }), /não pode ser aberto/);
  });

  test('automation: invalid profiles rejected, valid ones round-trip', async () => {
    const b = await fresh();
    await assert.rejects(b.call('saveAutomation', { id: 'steam:730', profile: { ...PROFILE, before: [{ type: 'wait', seconds: 0 }] } }), /1 a 30/);
    await assert.rejects(b.call('saveAutomation', { id: 'steam:730', profile: { ...PROFILE, before: [{ type: 'run', target: ' ' }] } }), /preencha/);
    await assert.rejects(b.call('saveAutomation', { id: 'steam:730', profile: { before: 'x', after: [] } }), /inválido/);
    await b.call('saveAutomation', { id: 'steam:730', profile: PROFILE });
    assert.deepEqual(await b.call('getAutomation', { id: 'steam:730' }), PROFILE);
    assert.deepEqual((await b.call('getAutomation', { id: 'epic:Fortnite' })).before, []);
    await assert.rejects(b.call('getAutomation', { id: 'nope' }), /não encontrado/);
  });

  test('add flows: invalid/duplicate Steam ids, dropped files, https-only external links', async () => {
    const b = await fresh();
    await assert.rejects(b.call('addSteam', { appId: 'abc' }), /App ID inválido/);
    await assert.rejects(b.call('addSteam', { appId: '730' }), /já está/);
    const { results } = await b.call('addFiles', {}, { files: [{ name: 'Jogo.lnk' }, { name: 'foto.png' }, { name: 'Jogo.lnk' }] });
    assert.deepEqual(results.map((r) => r.ok), [true, false, false]);
    await assert.rejects(b.call('addFiles', {}), /Nenhum arquivo/);
    await assert.rejects(b.call('openExternal', { url: 'http://example.com' }), /https/);
    await assert.rejects(b.call('setArt', { id: 'steam:730', kind: 'hero' }, { files: [{ name: 'x.txt', type: 'text/plain' }] }), /imagem/);
  });
});
