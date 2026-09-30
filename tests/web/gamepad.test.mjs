// Controller selection: one physical pad exposed twice (XInput "standard" + raw HID) must not scramble buttons.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb } from './_setup.mjs';

const { usablePads } = await importWeb('js/input/gamepad.js');

const pad = (index, mapping, id = 'pad') => ({ index, mapping, id, buttons: [], axes: [] });

test('uses only standard-mapped pads when one exists (ignores the raw duplicate)', () => {
  const xinput = pad(0, 'standard', 'Xbox 360 Controller (XInput STANDARD GAMEPAD)');
  const hid = pad(1, '', 'Controller (HID)');
  assert.deepEqual(usablePads([xinput, hid]).map((p) => p.index), [0]);
  assert.deepEqual(usablePads([hid, null, xinput]).map((p) => p.index), [0]);
});

test('keeps every standard pad (two real controllers)', () => {
  assert.deepEqual(usablePads([pad(0, 'standard'), pad(2, 'standard')]).map((p) => p.index), [0, 2]);
});

test('falls back to the first pad when none is standard', () => {
  assert.deepEqual(usablePads([null, pad(3, ''), pad(4, '')]).map((p) => p.index), [3]);
  assert.deepEqual(usablePads([null, null]), []);
});
