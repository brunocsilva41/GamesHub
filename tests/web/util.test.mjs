import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb, mockTimers } from './_setup.mjs';

const U = await importWeb('js/util.js');
const NOW = Date.parse('2025-06-15T12:00:00.000Z');
const ago = (ms) => new Date(NOW - ms).toISOString();
const MIN = 60e3, HOUR = 3600e3, DAY = 86400e3;

describe('fmtDuration (pt-BR)', () => {
  for (const [sec, want] of [
    [0, '—'], [-5, '—'], [null, '—'], ['abc', '—'], [59, 'menos de 1 min'], [60, '1 min'], [3599, '59 min'],
    [3600, '1 h'], [3660, '1 h 1 min'], [12 * 3600 + 300, '12 h 5 min'], [99 * 3600 + 59 * 60, '99 h 59 min'],
    [100 * 3600 + 1800, '100 h'], [320 * 3600, '320 h'], ['7200', '2 h'],
  ]) test(`${JSON.stringify(sec)} -> ${want}`, () => assert.equal(U.fmtDuration(sec), want));
});

describe('fmtRelative (pt-BR)', () => {
  const cases = [
    [null, 'Nunca'], ['garbage', 'Nunca'], [ago(-5 * MIN), 'agora mesmo'], [ago(30e3), 'agora mesmo'],
    [ago(5 * MIN), 'há 5 min'], [ago(59 * MIN), 'há 59 min'], [ago(HOUR), 'há 1 hora'], [ago(3 * HOUR), 'há 3 horas'],
    [ago(23 * HOUR + 59 * MIN), 'há 23 horas'], [ago(DAY), 'ontem'], [ago(47 * HOUR), 'ontem'], [ago(3 * DAY), 'há 3 dias'],
    [ago(7 * DAY), 'há 1 semana'], [ago(13 * DAY), 'há 1 semana'], [ago(14 * DAY), 'há 2 semanas'], [ago(29 * DAY), 'há 4 semanas'],
  ];
  for (const [iso, want] of cases) test(`${iso} -> ${want}`, () => assert.equal(U.fmtRelative(iso, NOW), want));
  test('30+ days falls back to an absolute pt-BR date', () => {
    const s = U.fmtRelative('2025-01-10T12:00:00.000Z', NOW);
    assert.match(s, /2025/);
    assert.match(s, /jan/i);
    assert.equal(s, U.fmtDate('2025-01-10T12:00:00.000Z'));
  });
  test('fmtDate handles empty/invalid input', () => {
    assert.equal(U.fmtDate(''), '—');
    assert.equal(U.fmtDate('nope'), '—');
  });
});

describe('fmtBytes (pt-BR)', () => {
  for (const [n, want] of [
    [0, '—'], [-1, '—'], [null, '—'], [512, '512 B'], [1024, '1 KB'], [1536, '1,5 KB'],
    [9.5 * 1024 ** 3, '9,5 GB'], [12.4 * 1024 ** 3, '12 GB'] /* one decimal only below 10 */, [34 * 1024 ** 3, '34 GB'], [2.5 * 1024 ** 4, '2,5 TB'], [3 * 1024 ** 5, '3.072 TB'],
  ]) test(`${n} -> ${want}`, () => assert.equal(U.fmtBytes(n), want));
});

describe('text helpers', () => {
  test('normalize strips accents, lowercases, trims', () => {
    assert.equal(U.normalize('  Pokémon ÉDIÇÃO  '), 'pokemon edicao');
    assert.equal(U.normalize(null), '');
  });
  test('esc escapes HTML', () => {
    assert.equal(U.esc(`<a href="x">'&'</a>`), '&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;');
    assert.equal(U.esc(undefined), '');
  });
  test('monogram', () => {
    assert.equal(U.monogram('Counter-Strike 2'), 'CS');
    assert.equal(U.monogram('Fortnite'), 'FO');
    assert.equal(U.monogram('  '), '?');
    assert.equal(U.monogram('élden ring'), 'ÉR');
  });
  test('plural', () => {
    assert.equal(U.plural(1, 'jogo', 'jogos'), '1 jogo');
    assert.equal(U.plural(0, 'jogo', 'jogos'), '0 jogos');
  });
  test('platformColor falls back to PC', () => {
    assert.equal(U.platformColor('Steam'), U.PLATFORM_COLORS.Steam);
    assert.equal(U.platformColor('Desconhecida'), U.PLATFORM_COLORS.PC);
  });
});

describe('gradient determinism', () => {
  test('hashStr is a stable 32-bit FNV-1a', () => {
    assert.equal(U.hashStr(''), 2166136261);
    assert.equal(U.hashStr('a'), 0xe40c292c);
    assert.equal(U.hashStr('steam:730'), U.hashStr('steam:730'));
    assert.ok(U.hashStr('x') >= 0 && U.hashStr('x') <= 0xffffffff);
  });
  test('gradientFor is deterministic and uses the palette', () => {
    const ids = Array.from({ length: 200 }, (_, i) => `folder:game-${i}`);
    const first = ids.map(U.gradientFor);
    assert.deepEqual(ids.map(U.gradientFor), first);
    for (const g of first) assert.match(g, /^linear-gradient\(135deg, #[0-9a-f]{6} 0%, #[0-9a-f]{6} 100%\)$/);
    assert.ok(new Set(first).size >= 6, 'spreads over the palette');
  });
});

describe('debounce', () => {
  test('coalesces calls, flush() runs now, cancel() drops', (t) => {
    mockTimers(t);
    const calls = [];
    const d = U.debounce((v) => calls.push(v), 100);
    d(1); d(2); d(3);
    t.mock.timers.tick(99);
    assert.deepEqual(calls, []);
    t.mock.timers.tick(1);
    assert.deepEqual(calls, [3]);
    d(4); d.flush(5);
    t.mock.timers.tick(200);
    assert.deepEqual(calls, [3, 5]);
    d(6); d.cancel();
    t.mock.timers.tick(200);
    assert.deepEqual(calls, [3, 5]);
  });
});

describe('isTextInput', () => {
  test('recognises editable elements', () => {
    assert.equal(U.isTextInput(null), false);
    assert.equal(U.isTextInput({ tagName: 'TEXTAREA' }), true);
    assert.equal(U.isTextInput({ tagName: 'INPUT', type: 'search' }), true);
    assert.equal(U.isTextInput({ tagName: 'INPUT', type: 'checkbox' }), false);
    assert.equal(U.isTextInput({ tagName: 'DIV', isContentEditable: true }), true);
  });
});
