// Generic controllers (e.g. ShanWan "Android Gamepad", VID 2563) report their own button order.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb } from './_setup.mjs';

const { normalizePad, hatDirections, layoutFor, STD } = await importWeb('js/input/padmap.js');

const ID = 'Android Gamepad (Vendor: 2563 Product: 0526)';
function androidPad(pressedRaw = [], hat = 1.2857) {
  const buttons = Array.from({ length: 15 }, (_, i) => ({ pressed: pressedRaw.includes(i), value: pressedRaw.includes(i) ? 1 : 0 }));
  const axes = [0, 0, 0, 0, 0, 0, 0, 0, 0, hat];
  return { index: 0, id: ID, mapping: '', buttons, axes };
}
const pressed = (p) => p.buttons.map((b, i) => (b.pressed ? i : -1)).filter((i) => i >= 0);

test('recognises the ShanWan/Android layout', () => {
  assert.equal(layoutFor(androidPad()), 'android');
});

test('X (raw 3) is X, not Y — the bug the user saw', () => {
  assert.deepEqual(pressed(normalizePad(androidPad([3]))), [STD.X]);
});

test('RT (raw 9) is RT, not Start; Start (raw 11) is Start', () => {
  assert.deepEqual(pressed(normalizePad(androidPad([9]))), [STD.RT]);
  assert.deepEqual(pressed(normalizePad(androidPad([11]))), [STD.START]);
});

test('A, B, Y, LB, RB, View map correctly', () => {
  const p = normalizePad(androidPad([0, 1, 4, 6, 7, 10]));
  assert.deepEqual(pressed(p).sort((a, b) => a - b), [STD.A, STD.B, STD.Y, STD.LB, STD.RB, STD.VIEW].sort((a, b) => a - b));
});

test('D-pad comes from the hat axis (8 directions, released ≈ 1.29)', () => {
  assert.deepEqual(hatDirections(-1), ['up']);
  assert.deepEqual(hatDirections(-1 + 2 / 7 * 2), ['right']);
  assert.deepEqual(hatDirections(-1 + 2 / 7 * 4), ['down']);
  assert.deepEqual(hatDirections(-1 + 2 / 7 * 6), ['left']);
  assert.deepEqual(hatDirections(1.2857), []);
  assert.deepEqual(pressed(normalizePad(androidPad([], -1))), [STD.UP]);
  assert.deepEqual(pressed(normalizePad(androidPad([], -1 + 2 / 7 * 4))), [STD.DOWN]);
});

test('standard pads pass through untouched', () => {
  const std = { index: 1, id: 'Xbox 360 Controller (XInput STANDARD GAMEPAD)', mapping: 'standard', buttons: [{ pressed: true, value: 1 }], axes: [0.5, 0] };
  const n = normalizePad(std);
  assert.equal(n.layout, 'standard');
  assert.equal(n.buttons, std.buttons);
});
