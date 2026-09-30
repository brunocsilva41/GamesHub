// "Encontrar jogos no PC": pure view helpers (discover-model.js) and the discoverGames/addDiscovered mock round-trip.
import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb, readRepo, unrefLongTimers, csFields, sortedKeys } from './_setup.mjs';

const M = await importWeb('js/views/discover-model.js');
const { connect } = await importWeb('js/bridge.js');
const { DEMO_CANDIDATES } = await importWeb('js/mock-discovery.js');
const CS_FIELDS = csFields(readRepo('src/GamesHub/Integrations/Discovery/DiscoveredGame.cs'), 'DiscoveredGame').sort();
const GB = 1073741824;

const c = (name, confidence, over = {}) => ({
  name, exe: `D:\\Jogos\\${name}\\${name}.exe`, installDir: `D:\\Jogos\\${name}`, source: 'folder', confidence,
  reasons: ['Unity', 'Pasta de jogos'], sizeBytes: -1, publisher: '', ...over,
});

describe('discover-model', () => {
  test('preselection threshold and confidence label', () => {
    assert.equal(M.CONFIDENT, 0.8);
    assert.equal(M.isPreselected(c('a', 0.8)), true);
    assert.equal(M.isPreselected(c('a', 0.79)), false);
    assert.equal(M.fmtConfidence(c('a', 0.873)), '87%');
    assert.equal(M.fmtConfidence(c('a', 1.7)), '100%');
    assert.equal(M.fmtConfidence({}), '0%');
  });

  test('groups by confidence (high first), sorted by confidence then name; empty groups dropped', () => {
    const list = [c('Zeta', 0.9), c('Beta', 0.55), c('Alfa', 0.9), c('Gama', 1)];
    const groups = M.groupCandidates(list);
    assert.deepEqual(groups.map((g) => g.key), ['high', 'low']);
    assert.deepEqual(groups[0].items.map((x) => x.c.name), ['Gama', 'Alfa', 'Zeta']);
    assert.deepEqual(groups[0].items.map((x) => x.i), [3, 2, 0], 'items keep their index in the original list');
    assert.deepEqual(M.groupCandidates([c('x', 0.6)]).map((g) => g.key), ['low']);
    assert.deepEqual(M.groupCandidates([]), []);
    assert.deepEqual([...M.initialSelection(list)], [0, 2, 3]);
  });

  test('reasons, size and source are summarised compactly', () => {
    assert.equal(M.reasonsLabel(['A', 'B', 'C', 'D', 'E']), 'A · B · C +2');
    assert.equal(M.reasonsLabel([]), '');
    assert.equal(M.metaLine(c('x', 1, { reasons: ['GOG'], sizeBytes: 48 * GB, source: 'registry' })), 'GOG — 48 GB — Programas instalados');
    assert.equal(M.metaLine(c('x', 1, { reasons: ['Xbox / Microsoft Store'], source: 'xbox' })), 'Xbox / Microsoft Store — Xbox / Microsoft Store');
    assert.equal(M.sourceLabel('???'), 'Pasta no disco');
  });

  test('payload uses edited names (trimmed), falls back to the discovered name, ignores stale indexes', () => {
    const list = [c('Um', 1), c('Dois', 0.9), c('Três', 0.6)];
    const names = new Map([[1, '  Dois (GOTY)  '], [2, '   ']]);
    assert.deepEqual(M.buildPayload(list, new Set([2, 1, 9]), names), [
      { exe: list[1].exe, name: 'Dois (GOTY)' },
      { exe: list[2].exe, name: 'Três' },
    ]);
    assert.deepEqual(M.buildPayload(list, new Set()), []);
  });

  test('removeAdded drops the added candidates and re-indexes selection and names', () => {
    const list = [c('A', 1), c('B', 1), c('C', 1), c('D', 0.6)];
    const names = new Map([[2, 'C editado'], [3, 'D editado']]);
    const next = M.removeAdded(list, new Set([0, 2, 3]), names, [{ ok: true }, { ok: false }, { ok: true }]);
    assert.equal(next.added, 2);
    assert.deepEqual(next.list.map((x) => x.name), ['B', 'C']);
    assert.deepEqual([...next.selected], [1]);
    assert.deepEqual([...next.names], [[1, 'C editado']]);
    assert.equal(M.removeAdded(list, new Set([0]), new Map(), null).added, 0);
  });

  test('markup escapes names/paths, marks the checkbox and keeps every control keyboard/gamepad reachable', () => {
    const evil = c('<img src=x onerror=alert(1)>', 0.95, { exe: 'D:\\"x"\\a.exe' });
    const html = M.candidateHtml(evil, 4, true);
    assert.doesNotMatch(html, /<img/);
    assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/);
    assert.match(html, /D:\\&quot;x&quot;\\a\.exe/);
    assert.match(html, /type="checkbox" data-nav data-disc-pick="4"[^>]* checked/);
    assert.match(html, /data-nav data-disc-name="4"/);
    assert.match(html, />95%</);
    const all = M.resultsHtml([c('A', 1), c('B', 0.5)], new Set([0]));
    assert.match(all, /Muito provavelmente jogos \(1\)/);
    assert.match(all, /Talvez sejam jogos \(1\)/);
    assert.equal((all.match(/ checked/g) || []).length, 1);
  });
});

describe('discovery mock round-trip', () => {
  let restoreTimers;
  before(() => { delete globalThis.chrome; restoreTimers = unrefLongTimers(); });
  after(() => restoreTimers());

  test('discoverGames streams progress, returns DiscoveredGame-shaped candidates and skips library games', async () => {
    const b = await connect();
    const progress = [];
    b.on('discoverProgress', (d) => progress.push(d.text));
    const { candidates } = await b.call('discoverGames');
    assert.ok(progress.length >= 3 && progress.at(-1) === 'Concluído.', progress.join(' | '));
    assert.ok(candidates.length >= 5);
    for (const x of candidates) {
      assert.deepEqual(sortedKeys(x), CS_FIELDS);
      assert.ok(x.confidence >= 0.5 && x.confidence <= 1 && ['registry', 'folder', 'xbox'].includes(x.source));
    }
    assert.ok(DEMO_CANDIDATES.some((x) => x.name === 'The Witcher 3: Wild Hunt'));
    assert.ok(!candidates.some((x) => x.name === 'The Witcher 3: Wild Hunt'), 'already in the demo library');
    assert.ok(candidates.some((x) => x.confidence < 0.8) && candidates.some((x) => x.confidence >= 0.8), 'both groups are demoed');
  });

  test('addDiscovered adds only allowlisted executables, with the edited name', async () => {
    const b = await connect();
    await assert.rejects(b.call('addDiscovered', { items: [] }), /Nenhum jogo selecionado/);
    const { candidates } = await b.call('discoverGames');
    const [first, second] = candidates;
    const { results } = await b.call('addDiscovered', { items: [
      { exe: first.exe, name: '  Meu Jogo  ' },
      { exe: second.exe.toUpperCase(), name: '' },
      { exe: 'C:\\Windows\\System32\\cmd.exe', name: 'x' },
    ] });
    assert.equal(results.length, 3);
    assert.deepEqual(results.map((r) => r.ok), [true, true, false]);
    for (const r of results) assert.deepEqual(sortedKeys(r), ['gameId', 'message', 'ok', 'undoToken']);
    assert.match(results[2].message, /última busca/);
    const games = (await b.call('getState')).games.map((g) => g.name);
    assert.ok(games.includes('Meu Jogo') && games.includes(second.name));
    const again = await b.call('addDiscovered', { items: [{ exe: first.exe, name: 'Meu Jogo' }] });
    assert.match(again.results[0].message, /já está na biblioteca/);
    const fresh = await connect();
    const r = await fresh.call('addDiscovered', { items: [{ exe: first.exe, name: 'x' }] });
    assert.equal(r.results[0].ok, false, 'a new host has no allowlist until it discovers');
  });
});
