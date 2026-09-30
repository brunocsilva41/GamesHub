// Gamepad (standard mapping). Polls with requestAnimationFrame only while a pad is connected
// and the window is visible.
//   D-pad / left stick: navigate (with repeat) · A: jogar · X: detalhes · Y: favoritar · B: voltar
//   LB / RB: seção anterior / próxima · Start: Big Picture · View/Select: buscar
//
// One physical controller is often exposed TWICE (XInput "standard" + a raw HID device with its own button
// order, e.g. through Steam Input, DS4Windows or vendor drivers). Mixing both scrambles the buttons (X acting
// as Y, B as LT…), so only pads with the W3C "standard" mapping are used; a non-standard pad is used alone
// only when no standard one exists. Steam can also translate the pad into arrow/Enter/Esc keys: those
// duplicates are ignored right after a real gamepad action (see padActedRecently()).
import { store } from '../store.js';
import { getBridge } from '../actions.js';
import * as C from './commands.js';
import { normalizePad } from './padmap.js';

const BTN = { A: 0, B: 1, X: 2, Y: 3, LB: 4, RB: 5, VIEW: 8, START: 9, UP: 12, DOWN: 13, LEFT: 14, RIGHT: 15 };
const REPEAT_DELAY = 380, REPEAT_RATE = 110, STICK = 0.55, TRIGGER = 0.5, DEDUPE_MS = 220;
// Analog buttons (triggers) report `pressed` at very low values on some pads: require a real press.
const isDown = (btn) => !!btn && (typeof btn.value === 'number' && btn.value > 0 && btn.value < 1 ? btn.value > TRIGGER : !!btn.pressed);

let lastPadAction = 0;
/** True for a short moment after the gamepad did something: keyboard echoes of it must be ignored. */
export const padActedRecently = () => performance.now() - lastPadAction < DEDUPE_MS;

/** Pads to read: every "standard" pad, or the first pad if none is standard. */
export function usablePads(all) {
  const pads = all.filter(Boolean);
  const standard = pads.filter((p) => p.mapping === 'standard');
  return standard.length ? standard : pads.slice(0, 1);
}

export function initGamepad() {
  if (!('getGamepads' in navigator)) return;
  let raf = 0;
  const prev = new Map();      // "padIndex:button" -> pressed
  const held = new Map();      // direction -> { since, last }
  let primed = false;          // first poll only records state: buttons held at connect are not presses

  const connected = () => Array.from(navigator.getGamepads()).some(Boolean);
  const setActive = (on) => { if (store.state.padActive !== on) store.set({ padActive: on }); };
  const acted = () => { lastPadAction = performance.now(); setActive(true); };

  function press(i) {
    acted();
    switch (i) {
      case BTN.A: C.activate(); break;
      case BTN.B: C.back(); break;
      case BTN.X: C.details(); break;
      case BTN.Y: C.favorite(); break;
      case BTN.LB: C.cycleSection(-1); break;
      case BTN.RB: C.cycleSection(1); break;
      case BTN.START: C.toggleBigPicture(); break;
      case BTN.VIEW: C.search(); break;
    }
  }

  function direction(pad) {
    const b = (i) => isDown(pad.buttons[i]);
    const x = pad.axes[0] || 0, y = pad.axes[1] || 0;
    if (b(BTN.UP) || y < -STICK) return 'up';
    if (b(BTN.DOWN) || y > STICK) return 'down';
    if (b(BTN.LEFT) || x < -STICK) return 'left';
    if (b(BTN.RIGHT) || x > STICK) return 'right';
    return null;
  }

  function frame(now) {
    raf = 0;
    if (document.hidden) return;
    const raw = usablePads(Array.from(navigator.getGamepads()));
    if (!raw.length) { primed = false; setActive(false); return; }
    let dir = null;
    const down = new Map(); // "pad:button" -> button index (per pad: two real controllers don't merge presses)
    for (const r of raw) {
      if (r.mapping !== 'standard') logRawPresses(r);
      const pad = normalizePad(r); // generic/Android/DirectInput controllers → standard button order
      pad.buttons.forEach((btn, i) => { if (isDown(btn)) down.set(`${pad.index}:${i}`, i); });
      dir = dir || direction(pad);
    }
    // Pads are global: WebView2 keeps delivering input while another app (e.g. the game) has focus.
    // Only act while our window is focused; otherwise just track state so held buttons don't fire on refocus.
    if (!primed || !document.hasFocus()) {
      primed = true;
      prev.clear();
      held.clear();
      for (const k of down.keys()) prev.set(k, true);
      if (dir) held.set(dir, { since: now, last: now + 1e9 });
      schedule();
      return;
    }
    for (const [k, i] of down) if (!prev.get(k) && ![BTN.UP, BTN.DOWN, BTN.LEFT, BTN.RIGHT].includes(i)) press(i);
    prev.clear();
    for (const k of down.keys()) prev.set(k, true);

    if (dir) {
      const h = held.get(dir);
      if (!h) { held.clear(); held.set(dir, { since: now, last: now }); acted(); C.navigate(dir); }
      else if (now - h.since > REPEAT_DELAY && now - h.last > REPEAT_RATE) { h.last = now; acted(); C.navigate(dir); }
    } else held.clear();

    schedule();
  }

  function schedule() {
    if (!raf && !document.hidden && connected()) raf = requestAnimationFrame(frame);
  }

  // Diagnostics for non-standard controllers: each raw button number is logged once with what it became,
  // so a wrong layout can be fixed from a user's log.
  const seenRaw = new Set();
  function logRawPresses(r) {
    const layout = normalizePad(r).layout;
    r.buttons.forEach((b, i) => {
      const key = `${r.id}:${i}`;
      if (!b?.pressed || seenRaw.has(key)) return;
      seenRaw.add(key);
      try { getBridge().call('log', { level: 'info', msg: `gamepad raw: "${r.id}" layout=${layout} button ${i} pressed` }).catch(() => {}); } catch { /* bridge not ready */ }
    });
  }

  /** Writes which controllers Windows exposes to the app log (diagnoses duplicated/remapped pads). */
  function logPads(reason) {
    const all = Array.from(navigator.getGamepads()).filter(Boolean);
    const used = new Set(usablePads(all).map((p) => p.index));
    const text = all.map((p) => `#${p.index} "${p.id}" mapping=${p.mapping || 'none'} buttons=${p.buttons.length} axes=${p.axes.length} layout=${normalizePad(p).layout}${used.has(p.index) ? ' [used]' : ' [ignored]'}`).join(' | ');
    try { getBridge().call('log', { level: 'info', msg: `gamepad ${reason}: ${text || 'none'}` }).catch(() => {}); } catch { /* bridge not ready */ }
  }

  // A connected pad alone does not show the controller hints: only real input does (press()/navigate).
  window.addEventListener('gamepadconnected', () => { logPads('connected'); schedule(); });
  window.addEventListener('gamepaddisconnected', () => { logPads('disconnected'); if (!connected()) setActive(false); });
  document.addEventListener('visibilitychange', () => { if (!document.hidden) schedule(); });
  // Mouse/keyboard use hides controller hints again (not the keyboard echoes Steam makes of pad presses).
  for (const type of ['mousedown', 'wheel']) window.addEventListener(type, () => setActive(false), { passive: true });
  window.addEventListener('mousemove', (e) => { if (Math.abs(e.movementX) + Math.abs(e.movementY) > 2) setActive(false); }, { passive: true });
  schedule();
}
