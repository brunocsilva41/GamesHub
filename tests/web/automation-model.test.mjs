import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb, readRepo } from './_setup.mjs';

const M = await importWeb('js/views/automation-model.js');

const OPTIONS = {
  powerPlans: [{ id: 'p1', name: 'Equilibrado' }],
  audioDevices: [{ id: 'a1', name: 'Fones' }],
  resolutions: [{ id: '1920x1080@60', name: '1920 × 1080' }],
};

describe('automation model', () => {
  test('blankAction defaults (wait gets 5 s)', () => {
    assert.deepEqual(M.blankAction(), { type: 'run', target: '', args: '', seconds: 0, enabled: true });
    assert.equal(M.blankAction('wait').seconds, 5);
  });

  test('normalizeProfile fills contract defaults and sanitises actions', () => {
    assert.deepEqual(M.normalizeProfile(null), { enabled: true, useDefault: true, before: [], after: [] });
    const p = M.normalizeProfile({
      enabled: false, useDefault: false,
      before: [{ type: 'bogus', target: 7 }, { type: 'wait', seconds: 99 }, { type: 'close', target: 'x.exe', args: 'ignored', enabled: false }],
      after: 'not a list',
    });
    assert.equal(p.enabled, false);
    assert.equal(p.useDefault, false);
    assert.deepEqual(p.before[0], { type: 'run', target: '7', args: '', seconds: 0, enabled: true });
    assert.equal(p.before[1].seconds, M.MAX_WAIT);
    assert.deepEqual(p.before[2], { type: 'close', target: 'x.exe', args: '', seconds: 0, enabled: false });
    assert.deepEqual(p.after, []);
    assert.equal(M.normalizeProfile({ before: [{ type: 'wait', seconds: -3.7 }] }).before[0].seconds, 0);
    assert.equal(M.normalizeProfile({ before: [{ type: 'wait', seconds: '2.6' }] }).before[0].seconds, 3);
  });

  test('profilePayload trims strings and drops args for non-run actions', () => {
    const p = M.profilePayload({
      enabled: 1, useDefault: 0,
      before: [{ type: 'run', target: '  C:\\x.exe ', args: ' -a ', seconds: 0, enabled: true }],
      after: [{ type: 'close', target: ' Discord.exe ', args: ' -z ', seconds: 0, enabled: true }],
    });
    assert.deepEqual(p, {
      enabled: true, useDefault: false,
      before: [{ type: 'run', target: 'C:\\x.exe', args: '-a', seconds: 0, enabled: true }],
      after: [{ type: 'close', target: 'Discord.exe', args: '', seconds: 0, enabled: true }],
    });
  });

  test('validateAction per type', () => {
    const v = (a, o = null) => M.validateAction({ ...M.blankAction(a.type), ...a }, o);
    assert.match(v({ type: 'run' }), /programa/);
    assert.equal(v({ type: 'run', target: 'x.exe' }), '');
    assert.match(v({ type: 'close', target: '  ' }), /processo/);
    assert.equal(v({ type: 'wait', seconds: 1 }), '');
    assert.equal(v({ type: 'wait', seconds: 30 }), '');
    assert.match(v({ type: 'wait', seconds: 0 }), /1 a 30/);
    assert.match(v({ type: 'wait', seconds: 31 }), /1 a 30/);
    assert.match(v({ type: 'powerPlan' }), /Escolha/);
    assert.equal(v({ type: 'powerPlan', target: 'p1' }, OPTIONS), '');
    assert.match(v({ type: 'audioDevice', target: 'gone' }, OPTIONS), /não está disponível/);
    assert.equal(v({ type: 'audioDevice', target: 'gone', enabled: false }, OPTIONS), '', 'disabled actions may keep a stale choice');
    assert.equal(v({ type: 'resolution', target: 'anything' }), '', 'no options loaded: no availability check');
    assert.match(M.validateAction({ type: 'nope', target: 'x' }), /desconhecido/);
  });

  test('validateProfile counts every problem', () => {
    const r = M.validateProfile({
      before: [M.blankAction('run'), { ...M.blankAction('run'), target: 'ok.exe' }],
      after: [{ ...M.blankAction('wait'), seconds: 0 }],
    });
    assert.equal(r.count, 2);
    assert.equal(r.before[1], '');
    assert.notEqual(r.after[0], '');
  });

  test('moveAction swaps within bounds and returns the new index', () => {
    const list = ['a', 'b', 'c'];
    assert.equal(M.moveAction(list, 0, -1), 0);
    assert.equal(M.moveAction(list, 0, 1), 1);
    assert.deepEqual(list, ['b', 'a', 'c']);
    assert.equal(M.moveAction(list, 2, 1), 2);
    assert.equal(M.moveAction(list, 2, -1), 1);
    assert.deepEqual(list, ['b', 'c', 'a']);
  });

  test('sameProfile ignores whitespace-only differences', () => {
    const a = { enabled: true, useDefault: true, before: [{ ...M.blankAction(), target: 'x.exe' }], after: [] };
    const b = { enabled: true, useDefault: true, before: [{ ...M.blankAction(), target: ' x.exe ' }], after: [] };
    assert.equal(M.sameProfile(a, b), true);
    assert.equal(M.sameProfile(a, { ...b, enabled: false }), false);
  });

  test('action types and option lists match the mock and the C# contract', () => {
    const keys = M.ACTION_TYPES.map((t) => t.key);
    const mockTypes = /const ACTION_TYPES = \[([^\]]*)\]/.exec(readRepo('web/js/mock-features.js'))[1].match(/'(\w+)'/g).map((s) => s.slice(1, -1));
    assert.deepEqual(keys, mockTypes);
    for (const t of M.RESTORED_TYPES) assert.ok(M.OPTION_LISTS[t], t);
    assert.deepEqual(Object.values(M.OPTION_LISTS).sort(), ['audioDevices', 'powerPlans', 'resolutions']);
  });
});
