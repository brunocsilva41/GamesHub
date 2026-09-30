// Translates any controller into the W3C "standard" layout the rest of the UI understands.
//
// Browsers only remap pads they recognise (XInput / known vendors → mapping "standard"). Many generic
// Xbox-style controllers (ShanWan, "Android Gamepad", PS3-style HID…) arrive with mapping "" and their own
// button order, so pressing X could report button 3 (= Y in the standard order) and RT button 9 (= Start).
// Known layouts are translated here; unknown ones fall back to the most common HID order.

/** Standard button indices (W3C Gamepad spec, "standard" remapping). */
export const STD = { A: 0, B: 1, X: 2, Y: 3, LB: 4, RB: 5, LT: 6, RT: 7, VIEW: 8, START: 9, L3: 10, R3: 11, UP: 12, DOWN: 13, LEFT: 14, RIGHT: 15, HOME: 16 };

/**
 * Raw → standard layouts. `buttons[i]` = standard index for raw button i (undefined = ignored).
 * `hat` = axis index of the D-pad "hat switch" (8 directions encoded in one axis), if the pad has one.
 */
const LAYOUTS = {
  // Android / "BUTTON_A,B,C,X,Y,Z,L1,R1,L2,R2,SELECT,START,MODE,THUMBL,THUMBR" — ShanWan (VID 2563) and most
  // generic wired/2.4 GHz controllers in Android/D-input mode.
  android: {
    buttons: [STD.A, STD.B, undefined, STD.X, STD.Y, undefined, STD.LB, STD.RB, STD.LT, STD.RT,
      STD.VIEW, STD.START, STD.HOME, STD.L3, STD.R3],
    hat: 9,
  },
  // Classic DirectInput order used by many PC HID pads (Logitech F310 "D" mode, PS3-style clones).
  dinput: {
    buttons: [STD.X, STD.A, STD.B, STD.Y, STD.LB, STD.RB, STD.LT, STD.RT, STD.VIEW, STD.START, STD.L3, STD.R3, STD.HOME],
    hat: 9,
  },
};

/** Which layout a non-standard pad uses (by name/vendor first, then by button count). */
export function layoutFor(pad) {
  const id = String(pad.id || '');
  if (/android gamepad|vendor:\s*2563|2563-0526|shanwan/i.test(id)) return 'android';
  if (/vendor:\s*046d|logitech|vendor:\s*0e8f|vendor:\s*0810/i.test(id)) return 'dinput';
  return pad.buttons.length >= 15 ? 'android' : 'dinput';
}

// Hat switch: one axis, 8 positions from -1 (up) clockwise in steps of 2/7; ~1.28 when released.
const HAT_DIRS = [['up'], ['up', 'right'], ['right'], ['down', 'right'], ['down'], ['down', 'left'], ['left'], ['up', 'left']];
export function hatDirections(value) {
  if (typeof value !== 'number' || value < -1.05 || value > 1.05) return [];
  const step = Math.round((value + 1) / (2 / 7));
  return HAT_DIRS[Math.min(7, Math.max(0, step))];
}

const pressedButton = (on) => ({ pressed: on, value: on ? 1 : 0 });

/**
 * A pad as the UI expects it: { index, id, mapping:'standard', buttons[17], axes[4], layout }.
 * Standard pads pass through untouched.
 */
export function normalizePad(pad) {
  if (pad.mapping === 'standard') return { index: pad.index, id: pad.id, buttons: pad.buttons, axes: pad.axes, layout: 'standard' };
  const layout = layoutFor(pad);
  const L = LAYOUTS[layout];
  const buttons = Array.from({ length: 17 }, () => pressedButton(false));
  pad.buttons.forEach((b, raw) => {
    const std = L.buttons[raw];
    if (std === undefined || !b) return;
    const cur = buttons[std];
    // Keep the strongest value if two raw buttons land on the same standard one.
    if ((b.value || 0) >= (cur.value || 0) || b.pressed) buttons[std] = { pressed: !!b.pressed || cur.pressed, value: Math.max(b.value || 0, cur.value || 0) };
  });
  for (const d of hatDirections(pad.axes[L.hat])) {
    buttons[{ up: STD.UP, down: STD.DOWN, left: STD.LEFT, right: STD.RIGHT }[d]] = pressedButton(true);
  }
  const axes = [pad.axes[0] || 0, pad.axes[1] || 0, pad.axes[2] || 0, pad.axes[5] ?? pad.axes[3] ?? 0];
  return { index: pad.index, id: pad.id, buttons, axes, layout };
}
