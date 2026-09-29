import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb, fakeHost, installWebview, quietConsole, unrefLongTimers, mockTimers } from './_setup.mjs';

const { connect, BridgeError } = await importWeb('js/bridge.js');

let uninstall = () => {};
afterEach(() => { uninstall(); uninstall = () => {}; });

async function bridgeWith(opts) {
  const host = fakeHost(opts);
  uninstall = installWebview(host);
  const bridge = await connect();
  return { host, bridge };
}

describe('bridge request/reply', () => {
  test('uses chrome.webview when present; posts the documented cmd envelope', async () => {
    const { host, bridge } = await bridgeWith();
    assert.equal(bridge.isMock, false);
    const p = bridge.call('launch', { id: 'steam:730' });
    assert.deepEqual(host.last(), { type: 'cmd', id: 1, name: 'launch', args: { id: 'steam:730' } });
    host.reply(1, true, { message: 'ok' });
    assert.deepEqual(await p, { message: 'ok' });
  });

  test('ids increase; replies are correlated by id, in any order', async () => {
    const { host, bridge } = await bridgeWith();
    const a = bridge.call('reveal', { id: 'a' });
    const b = bridge.call('reveal', { id: 'b' });
    const c = bridge.call('getState');
    assert.deepEqual(host.sent.map((m) => m.id), [1, 2, 3]);
    assert.deepEqual(host.sent[2].args, {}, 'args default to {}');
    host.reply(3, true, { which: 'c' });
    host.reply(1, true, { which: 'a' });
    host.reply(2, true, { which: 'b' });
    assert.deepEqual(await Promise.all([a, b, c]), [{ which: 'a' }, { which: 'b' }, { which: 'c' }]);
  });

  test('ok without data resolves {}; null args become {}', async () => {
    const { host, bridge } = await bridgeWith();
    const p = bridge.call('rescan', null);
    assert.deepEqual(host.last().args, {});
    host.deliver({ type: 'reply', id: 1, ok: true });
    assert.deepEqual(await p, {});
  });

  test('ok:false rejects with BridgeError("failed") carrying message and data', async () => {
    const { host, bridge } = await bridgeWith();
    const p = bridge.call('remove', { id: 'x' });
    host.deliver({ type: 'reply', id: 1, ok: false, error: 'Jogo não encontrado.', data: { gameId: 'x' } });
    await assert.rejects(p, (e) => e instanceof BridgeError && e.code === 'failed' && e.message === 'Jogo não encontrado.' && e.data.gameId === 'x');
    const q = bridge.call('remove', { id: 'y' });
    host.deliver({ type: 'reply', id: 2, ok: false });
    await assert.rejects(q, { name: 'BridgeError', message: 'Algo deu errado.' });
  });

  test('JSON-string messages are parsed; garbage, unknown ids and duplicate replies are ignored', async () => {
    const { host, bridge } = await bridgeWith();
    const p = bridge.call('checkUpdate');
    const { calls } = await quietConsole(() => {
      host.deliver('{not json');
      host.deliver(null);
      host.deliver(42);
      host.deliver({ type: 'reply', id: 999, ok: true, data: {} });
    });
    assert.equal(calls.filter(([m]) => m === 'warn').length, 1, 'invalid JSON is warned once');
    host.deliver(JSON.stringify({ type: 'reply', id: 1, ok: true, data: { available: false } }));
    host.deliver({ type: 'reply', id: 1, ok: false, error: 'late duplicate' });
    assert.deepEqual(await p, { available: false });
  });
});

describe('bridge timeouts', () => {
  test('explicit timeout rejects with code "timeout" and a late reply is ignored', async () => {
    const { host, bridge } = await bridgeWith();
    const p = bridge.call('getState', {}, { timeout: 25 });
    await assert.rejects(p, (e) => e instanceof BridgeError && e.code === 'timeout' && /não respondeu/.test(e.message));
    host.reply(1, true, { late: true }); // must not throw
    assert.equal(bridge._pending.size, 0);
  });

  test('default is 15 s; per-command table overrides it', async (t) => {
    const { host, bridge } = await bridgeWith();
    mockTimers(t);
    const results = {};
    const track = (name, p) => p.then(() => { results[name] = 'ok'; }, (e) => { results[name] = e.code; });
    const reveal = track('reveal', bridge.call('reveal', { id: 'x' }));      // default 15 000
    const rescan = track('rescan', bridge.call('rescan'));                   // 60 000
    const pick = track('pickFile', bridge.call('pickFile'));                  // 0 = never times out
    t.mock.timers.tick(14999);
    await Promise.resolve();
    assert.deepEqual(results, {});
    t.mock.timers.tick(1);
    await reveal;
    assert.equal(results.reveal, 'timeout');
    t.mock.timers.tick(45000);
    await rescan;
    assert.equal(results.rescan, 'timeout');
    t.mock.timers.tick(10 * 60 * 1000);
    await Promise.resolve();
    assert.equal(results.pickFile, undefined, 'native dialogs wait for the user');
    host.reply(3, true, { cancelled: true });
    await pick;
    assert.equal(results.pickFile, 'ok');
  });
});

describe('bridge transport', () => {
  test('postMessage throwing rejects with code "transport" and clears the pending entry', async () => {
    const { host, bridge } = await bridgeWith();
    host.throwOnPost = new Error('boom');
    await assert.rejects(bridge.call('launch', { id: 'x' }), (e) => e.code === 'transport' && /boom/.test(e.data));
    assert.equal(bridge._pending.size, 0);
  });

  test('files go through postMessageWithAdditionalObjects', async () => {
    const { host, bridge } = await bridgeWith();
    const files = [{ name: 'a.exe' }, { name: 'b.lnk' }];
    const p = bridge.call('addFiles', {}, { files });
    assert.deepEqual(host.files, [files]);
    host.reply(1, true, { results: [] });
    assert.deepEqual(await p, { results: [] });
  });

  test('files without postMessageWithAdditionalObjects -> transport error', async () => {
    const { bridge } = await bridgeWith({ withFiles: false });
    await assert.rejects(bridge.call('addFiles', {}, { files: [{ name: 'a.exe' }] }), { code: 'transport' });
  });

  test('log() sends a truncated "log" command and never rejects', async () => {
    const { host, bridge } = await bridgeWith();
    bridge.log('warn', 'x'.repeat(5000));
    assert.equal(host.last().name, 'log');
    assert.equal(host.last().args.level, 'warn');
    assert.equal(host.last().args.msg.length, 2000);
    host.reply(host.last().id, false, 'nope'); // swallowed
    await new Promise((r) => setImmediate(r));
  });
});

describe('bridge events', () => {
  test('events reach every handler; off() unsubscribes; a throwing handler does not break others', async () => {
    const { host, bridge } = await bridgeWith();
    const got = [];
    const off = bridge.on('games', (d) => got.push(['a', d.games.length]));
    bridge.on('games', () => { throw new Error('handler bug'); });
    bridge.on('games', (d) => got.push(['c', d.games.length]));
    const { calls } = await quietConsole(() => host.emit('games', { games: [1, 2] }));
    assert.deepEqual(got, [['a', 2], ['c', 2]]);
    assert.equal(calls.filter(([m]) => m === 'error').length, 1);
    off();
    await quietConsole(() => host.emit('games', { games: [] }));
    assert.deepEqual(got.map(([k]) => k), ['a', 'c', 'c']);
  });

  test('events without data get {}; events without a name are ignored', async () => {
    const { host, bridge } = await bridgeWith();
    const got = [];
    bridge.on('focusSearch', (d) => got.push(d));
    host.deliver({ type: 'event', name: 'focusSearch' });
    host.deliver({ type: 'event', data: {} });
    assert.deepEqual(got, [{}]);
  });
});

describe('connect() fallback', () => {
  test('without chrome.webview it uses the in-page mock', async () => {
    const restore = unrefLongTimers();
    try {
      const bridge = await connect();
      assert.equal(bridge.isMock, true);
      const st = await bridge.call('getState');
      assert.equal(st.demo, true);
      assert.ok(st.games.length > 5);
    } finally { restore(); }
  });
});
