// Settings › Discos: games grouped per drive with size and share of the disk.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb } from './_setup.mjs';

const { drivesHtml, groupByDrive, fmtPct } = await importWeb('js/views/drives.js');
const GB = 1024 ** 3;
const C = 'C:\\', D = 'D:\\';
const drives = [
  { name: C, label: 'Windows', totalBytes: 500 * GB, freeBytes: 100 * GB },
  { name: D, label: '', totalBytes: 1000 * GB, freeBytes: 400 * GB },
];
const games = [
  { id: 'a', name: 'Pequeno', platform: 'Steam', sizeBytes: 10 * GB, drive: D },
  { id: 'b', name: 'Grande', platform: 'Epic', sizeBytes: 150 * GB, drive: 'd:\\' },
  { id: 'c', name: 'Medindo', platform: 'PC', sizeBytes: -1, drive: D },
  { id: 'd', name: 'Sem pasta', platform: 'Steam', sizeBytes: -1, drive: '' },
  { id: 'e', name: 'Disco removido', platform: 'PC', sizeBytes: 5 * GB, drive: 'Z:\\' },
];

test('groups by drive (case-insensitive), largest first, unknown sizes last', () => {
  const { byRoot, unknown } = groupByDrive(drives, games);
  assert.deepEqual(byRoot.get(D).map((g) => g.id), ['b', 'a', 'c']);
  assert.deepEqual(byRoot.get(C), []);
  assert.deepEqual(unknown.map((g) => g.id).sort(), ['d', 'e']);
});

test('drive summary shows count, total size and share of the disk', () => {
  const html = drivesHtml({ drives, games });
  assert.match(html, /3 jogos · 160 GB \(16% do disco\) · medindo…/);
  assert.match(html, /Nenhum jogo nesta unidade/);
  assert.match(html, /Local desconhecido/);
  assert.match(html, /calculando…/);
});

test('each game shows its size and % of the drive', () => {
  const html = drivesHtml({ drives, games });
  assert.match(html, /Grande<\/button>[\s\S]*?150 GB[\s\S]*?15%/);
  assert.match(html, /Pequeno<\/button>[\s\S]*?10 GB[\s\S]*?1%/);
});

test('game names are escaped', () => {
  const html = drivesHtml({ drives, games: [{ id: 'x"><img src=x onerror=alert(1)>', name: '<script>alert(1)</script>', sizeBytes: GB, drive: C }] });
  assert.ok(!html.includes('<script>alert'), 'script tag must be escaped');
  assert.ok(!html.includes('<img src=x'), 'attribute injection must be escaped');
});

test('games sharing an install folder are counted once (game + mod launcher)', () => {
  const E = 'E:\\';
  const shared = [
    { id: 'bo3', name: 'Black Ops 3', sizeBytes: 134 * GB, sizeState: 'known', folder: 'e:\\steam\\bo3', drive: E },
    { id: 'cb', name: 'CB Servers Launcher', sizeBytes: 134 * GB, sizeState: 'known', folder: 'e:\\steam\\bo3', drive: E },
  ];
  const html = drivesHtml({ drives: [{ name: E, label: '', totalBytes: 466 * GB, freeBytes: 153 * GB }], games: shared });
  assert.match(html, /2 jogos · 134 GB \(29% do disco\)/, 'not 268 GB / 58%');
  assert.match(html, /mesma pasta de Black Ops 3/);
});

test('loose files in the same folder are not "shared installs"', () => {
  const html = drivesHtml({ drives, games: [
    { id: 'p', name: 'plutonium', sizeBytes: -1, sizeState: 'unavailable', folder: 'c:\\jogos', drive: C },
    { id: 't', name: 'TEKKEN 7', sizeBytes: -1, sizeState: 'unavailable', folder: 'c:\\jogos', drive: C },
  ] });
  assert.ok(!html.includes('mesma pasta'), 'no shared-folder note for loose files');
});

test('size states: unavailable vs still measuring', () => {
  const html = drivesHtml({ drives, games: [
    { id: 'u', name: 'Solto', sizeBytes: -1, sizeState: 'unavailable', drive: C },
    { id: 'm', name: 'Grande HDD', sizeBytes: -1, sizeState: 'measuring', drive: D },
  ] });
  assert.match(html, /tamanho indisponível/);
  assert.match(html, /1 jogo · tamanho indisponível/);
  assert.match(html, /1 jogo · medindo tamanhos…/);
  assert.ok(!/— \(0% do disco\)/.test(html), 'no "— (0%)" summary');
});

test('percent formatting (pt-BR)', () => {
  assert.equal(fmtPct(0.05), '< 0,1%');
  assert.equal(fmtPct(1.26), '1,3%');
  assert.equal(fmtPct(42.4), '42%');
});

test('works with the previous response shape (no games list)', () => {
  assert.match(drivesHtml({ drives }), /Nenhum jogo nesta unidade/);
});
