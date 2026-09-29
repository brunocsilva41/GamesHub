// Gamepad (standard mapping). Polls with requestAnimationFrame only while a pad is connected
// and the window is visible.
//   D-pad / left stick: navigate (with repeat) · A: jogar · X: detalhes · Y: favoritar · B: voltar
//   LB / RB: seção anterior / próxima · Start: Big Picture · View/Select: buscar
import { store } from '../store.js';
import * as C from './commands.js';

const BTN = { A: 0, B: 1, X: 2, Y: 3, LB: 4, RB: 5, VIEW: 8, START: 9, UP: 12, DOWN: 13, LEFT: 14, RIGHT: 15 };
const REPEAT_DELAY = 380, REPEAT_RATE = 110, STICK = 0.55;

export function initGamepad() {
  if (!('getGamepads' in navigator)) return;
  let raf = 0;
  const prev = new Map();      // button index -> pressed
  const held = new Map();      // direction -> { since, last }

  const connected = () => Array.from(navigator.getGamepads()).some(Boolean);
  const setActive = (on) => { if (store.state.padActive !== on) store.set({ padActive: on }); };

  function press(i) {
    setActive(true);
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
    const b = (i) => !!pad.buttons[i]?.pressed;
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
    const pads = Array.from(navigator.getGamepads()).filter(Boolean);
    if (!pads.length) { setActive(false); return; }
    let dir = null;
    const pressed = new Set();
    for (const pad of pads) {
      pad.buttons.forEach((btn, i) => { if (btn.pressed) pressed.add(i); });
      dir = dir || direction(pad);
    }
    for (const i of pressed) if (!prev.get(i) && ![BTN.UP, BTN.DOWN, BTN.LEFT, BTN.RIGHT].includes(i)) press(i);
    prev.clear();
    for (const i of pressed) prev.set(i, true);

    if (dir) {
      const h = held.get(dir);
      if (!h) { held.clear(); held.set(dir, { since: now, last: now }); setActive(true); C.navigate(dir); }
      else if (now - h.since > REPEAT_DELAY && now - h.last > REPEAT_RATE) { h.last = now; C.navigate(dir); }
    } else held.clear();

    schedule();
  }

  function schedule() {
    if (!raf && !document.hidden && connected()) raf = requestAnimationFrame(frame);
  }

  window.addEventListener('gamepadconnected', () => { setActive(true); schedule(); });
  window.addEventListener('gamepaddisconnected', () => { if (!connected()) setActive(false); });
  document.addEventListener('visibilitychange', () => { if (!document.hidden) schedule(); });
  // Mouse/keyboard use hides controller hints again.
  window.addEventListener('mousemove', () => setActive(false), { passive: true });
  schedule();
}
