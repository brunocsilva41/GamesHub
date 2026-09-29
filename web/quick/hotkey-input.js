// <gh-hotkey-input> — framework-free web component that records a global-hotkey combination.
// Output format matches the C# parser QuickHotkey (CORE's HotkeyParser accepts a subset): "Ctrl+Alt+Shift+Win+Key",
// modifiers in that order; keys: A–Z, 0–9, F1–F24, Space, Enter, Tab, Up/Down/Left/Right, Home, End,
// PageUp, PageDown, Insert, Delete, Pause, PrintScreen, Num0–Num9.
//
// Usage:
//   import './quick/hotkey-input.js';
//   <gh-hotkey-input value="Ctrl+Shift+Space" label="Atalho da paleta rápida"></gh-hotkey-input>
//   el.addEventListener('change', (e) => save(e.detail.value));   // "" when cleared
// Attributes: value, label (accessible name), disabled, allow-empty (default true; set "false" to forbid clearing).
// While recording: Esc cancels, Backspace clears, a combo without Ctrl/Alt/Shift/Win is rejected (except F-keys).

const MOD_ORDER = ['Ctrl', 'Alt', 'Shift', 'Win'];
const NAMED = {
  ' ': 'Space', Spacebar: 'Space', Enter: 'Enter', Tab: 'Tab',
  ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
  Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
  Insert: 'Insert', Delete: 'Delete', Pause: 'Pause', PrintScreen: 'PrintScreen',
};
const ALIASES = {
  control: 'Ctrl', ctrl: 'Ctrl', ctl: 'Ctrl', alt: 'Alt', option: 'Alt', shift: 'Shift',
  win: 'Win', windows: 'Win', meta: 'Win', super: 'Win', cmd: 'Win',
};
const KEY_ALIASES = {
  space: 'Space', 'espaço': 'Space', espaco: 'Space', spacebar: 'Space', enter: 'Enter', return: 'Enter', tab: 'Tab',
  up: 'Up', down: 'Down', left: 'Left', right: 'Right', arrowup: 'Up', arrowdown: 'Down', arrowleft: 'Left', arrowright: 'Right',
  home: 'Home', end: 'End', pageup: 'PageUp', pagedown: 'PageDown', pgup: 'PageUp', pgdn: 'PageDown', pagedn: 'PageDown',
  insert: 'Insert', ins: 'Insert', delete: 'Delete', del: 'Delete', pause: 'Pause', break: 'Pause',
  printscreen: 'PrintScreen', prtsc: 'PrintScreen', printscrn: 'PrintScreen',
};
const KEY_LABELS = { Space: 'Espaço', Up: '↑', Down: '↓', Left: '←', Right: '→', Delete: 'Del', Insert: 'Ins', PrintScreen: 'PrtSc', PageUp: 'PgUp', PageDown: 'PgDn' };

const isFKey = (k) => /^F([1-9]|1[0-9]|2[0-4])$/.test(k);

/** Canonical key name from one token, or null. */
function canonicalKey(token) {
  const t = token.trim();
  if (/^[a-z0-9]$/i.test(t)) return t.toUpperCase();
  const f = /^f([1-9]|1[0-9]|2[0-4])$/i.exec(t);
  if (f) return 'F' + f[1];
  const n = /^num(?:pad)?([0-9])$/i.exec(t);
  if (n) return 'Num' + n[1];
  return KEY_ALIASES[t.toLowerCase()] || null;
}

/** Normalizes "ctrl + shift + space" → "Ctrl+Shift+Space"; returns null when invalid (same rules as C#). */
export function normalizeHotkey(text) {
  if (typeof text !== 'string' || !text.trim()) return null;
  const mods = new Set();
  let key = null;
  for (const raw of text.split('+')) {
    const p = raw.trim();
    if (!p) return null;
    const mod = ALIASES[p.toLowerCase()];
    if (mod) {
      if (mods.has(mod)) return null;
      mods.add(mod);
      continue;
    }
    if (key) return null;
    key = canonicalKey(p);
    if (!key) return null;
  }
  if (!key) return null;
  if (!mods.size && !isFKey(key)) return null;
  return [...MOD_ORDER.filter((m) => mods.has(m)), key].join('+');
}

/** Canonical key name for a KeyboardEvent, '' for a pure modifier, null when unsupported. */
export function keyFromEvent(e) {
  const k = e.key;
  if (['Control', 'Alt', 'Shift', 'Meta', 'OS', 'AltGraph'].includes(k)) return '';
  const code = e.code || '';
  if (/^Numpad[0-9]$/.test(code)) return 'Num' + code.slice(6);
  if (/^Digit[0-9]$/.test(code)) return code.slice(5);          // Shift+1 reports "!" as key
  if (/^[a-z]$/i.test(k)) return k.toUpperCase();
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (isFKey(k)) return k;
  if (NAMED[k]) return NAMED[k];
  if (code === 'Space') return 'Space';
  return null;
}

/** Full canonical combo from a KeyboardEvent (may be invalid, e.g. no modifier) + its parts. */
export function comboFromEvent(e) {
  const mods = [];
  if (e.ctrlKey) mods.push('Ctrl');
  if (e.altKey) mods.push('Alt');
  if (e.shiftKey) mods.push('Shift');
  if (e.metaKey) mods.push('Win');
  const key = keyFromEvent(e);
  return { mods, key, text: key ? [...mods, key].join('+') : mods.join('+') };
}

/** Friendly pt-BR labels for display chips: "Ctrl+Shift+Space" → ["Ctrl","Shift","Espaço"]. */
export function hotkeyLabels(value) {
  return (value || '').split('+').filter(Boolean).map((p) => KEY_LABELS[p] || p);
}

const STYLE = `
:host { display: inline-block; font: inherit; color: var(--text, #eceef5); }
:host([disabled]) { opacity: .5; pointer-events: none; }
button {
  all: unset; box-sizing: border-box; display: inline-flex; align-items: center; gap: 6px; flex-wrap: wrap;
  min-width: 190px; min-height: 36px; padding: 5px 10px; cursor: pointer;
  border: 1px solid var(--line-2, rgba(255,255,255,.12)); border-radius: var(--r-md, 8px);
  background: var(--surface-2, #1a1f2d); color: inherit; font: inherit; font-size: var(--fs-md, 13px);
  transition: border-color var(--dur-fast, 120ms) ease, box-shadow var(--dur-fast, 120ms) ease;
}
button:hover { border-color: var(--line-3, rgba(255,255,255,.2)); }
button:focus-visible { outline: none; box-shadow: var(--focus-ring, 0 0 0 2px #0b0d14, 0 0 0 4px #8b72ff); }
button.recording { border-color: var(--accent, #8b72ff); box-shadow: 0 0 0 3px var(--accent-soft, rgba(124,92,255,.16)); }
kbd {
  display: inline-block; min-width: 22px; padding: 1px 6px; text-align: center;
  border: 1px solid var(--line-2, rgba(255,255,255,.12)); border-bottom-width: 2px; border-radius: var(--r-xs, 4px);
  background: var(--surface-3, #222838); font: 600 12px/18px var(--font, 'Segoe UI', sans-serif); color: var(--text, #eceef5);
}
.plus { color: var(--text-3, #8a91a8); font-size: 11px; }
.placeholder { color: var(--text-3, #8a91a8); }
.recording .placeholder { color: var(--accent, #8b72ff); }
.hint { display: block; margin-top: 4px; min-height: 16px; font-size: var(--fs-xs, 11px); color: var(--text-3, #8a91a8); }
.hint.err { color: var(--danger, #f87171); }
@media (prefers-reduced-motion: reduce) { button { transition: none; } }
`;

export class GhHotkeyInput extends HTMLElement {
  static get observedAttributes() { return ['value', 'label', 'disabled']; }

  constructor() {
    super();
    const root = this.attachShadow({ mode: 'open', delegatesFocus: true });
    root.innerHTML = `<style>${STYLE}</style>
      <button type="button" part="field" aria-describedby="hint"></button>
      <span class="hint" id="hint" part="hint" aria-live="polite"></span>`;
    this._btn = root.querySelector('button');
    this._hint = root.querySelector('.hint');
    this._recording = false;
    this._value = '';
    this._btn.addEventListener('click', () => (this._recording ? this._stop() : this._start()));
    this._btn.addEventListener('keydown', (e) => this._onKeyDown(e));
    this._btn.addEventListener('keyup', (e) => this._onKeyUp(e));
    this._btn.addEventListener('blur', () => { if (this._recording) this._stop(); });
  }

  connectedCallback() {
    if (this.hasAttribute('value')) this._value = normalizeHotkey(this.getAttribute('value')) || '';
    this._render();
  }

  attributeChangedCallback(name, _old, val) {
    if (name === 'value') this._value = normalizeHotkey(val || '') || '';
    if (name === 'disabled' && this.hasAttribute('disabled') && this._recording) this._stop();
    this._render();
  }

  get value() { return this._value; }
  set value(v) { this._value = normalizeHotkey(String(v ?? '')) || ''; this._render(); }

  /** Shows an external error below the field (e.g. "já está em uso por outro programa"). */
  setError(text) { this._setHint(text || '', true); }

  focus(opts) { this._btn.focus(opts); }

  _start() {
    this._recording = true;
    this._setHint('Esc cancela · Backspace limpa', false);
    this._render(null);
  }

  _stop() {
    this._recording = false;
    this._setHint('', false);
    this._render();
  }

  _commit(value) {
    const changed = value !== this._value;
    this._value = value;
    this._stop();
    if (changed) this.dispatchEvent(new CustomEvent('change', { detail: { value }, bubbles: true, composed: true }));
  }

  _onKeyDown(e) {
    if (!this._recording) {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); this._start(); }
      else if (e.key === 'Backspace' || e.key === 'Delete') { if (this._allowEmpty()) { e.preventDefault(); this._commit(''); } }
      return;
    }
    e.preventDefault();
    e.stopPropagation();
    const combo = comboFromEvent(e);
    const bare = !combo.mods.length;
    if (bare && e.key === 'Escape') { this._stop(); return; }
    if (bare && e.key === 'Backspace') {
      if (this._allowEmpty()) this._commit('');
      else this._setHint('Um atalho é obrigatório.', true);
      return;
    }
    if (combo.key === '') { this._render(combo.mods); return; }        // only modifiers so far: live preview
    if (combo.key === null) { this._setHint('Tecla não suportada. Use letras, números, F1–F24, Espaço ou setas.', true); return; }
    const normalized = normalizeHotkey(combo.text);
    if (!normalized) {
      this._setHint('Use Ctrl, Alt, Shift ou Win junto com a tecla (exceto F1–F24).', true);
      this._render(combo.mods);
      return;
    }
    this._commit(normalized);
  }

  _onKeyUp(e) {
    if (!this._recording) return;
    e.preventDefault();
    this._render(comboFromEvent(e).mods);
  }

  _allowEmpty() { return this.getAttribute('allow-empty') !== 'false'; }

  _setHint(text, isError) {
    this._hint.textContent = text;
    this._hint.classList.toggle('err', !!isError && !!text);
  }

  /** previewMods: array while recording (null = none pressed yet); undefined = show the stored value. */
  _render(previewMods) {
    if (!this._btn) return;
    const label = this.getAttribute('label') || 'Atalho de teclado';
    this._btn.classList.toggle('recording', this._recording);
    this._btn.setAttribute('aria-pressed', String(this._recording));
    this._btn.replaceChildren();
    let parts;
    if (this._recording) {
      parts = previewMods && previewMods.length ? [...previewMods, null] : null;
      this._btn.setAttribute('aria-label', label + ': gravando. Pressione as teclas.');
    } else {
      parts = this._value ? hotkeyLabels(this._value) : [];
      this._btn.setAttribute('aria-label', label + ': ' + (this._value || 'nenhum') + '. Enter para alterar.');
    }
    if (this._recording && !parts) { this._placeholder('Pressione as teclas…'); return; }
    if (!this._recording && !parts.length) { this._placeholder('Nenhum atalho'); return; }
    parts.forEach((p, i) => {
      if (i) { const s = document.createElement('span'); s.className = 'plus'; s.textContent = '+'; this._btn.append(s); }
      if (p === null) { this._placeholder('…'); return; }
      const k = document.createElement('kbd');
      k.textContent = p;
      this._btn.append(k);
    });
  }

  _placeholder(text) {
    const s = document.createElement('span');
    s.className = 'placeholder';
    s.textContent = text;
    this._btn.append(s);
  }
}

if (!customElements.get('gh-hotkey-input')) customElements.define('gh-hotkey-input', GhHotkeyInput);
