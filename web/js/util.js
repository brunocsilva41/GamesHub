// Small helpers: DOM building, formatting (pt-BR), hashing. Import-safe outside the browser.

export const $ = (sel, root = document) => root.querySelector(sel);
export const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

/**
 * Element factory. attrs: class, text, html, style (object|string), dataset (object),
 * on<Event> (function), any other attribute (false/null = omitted, true = empty attribute).
 */
export function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'text') el.textContent = v;
    else if (k === 'html') el.innerHTML = v;
    else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
    else if (k === 'dataset') Object.assign(el.dataset, v);
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
    else el.setAttribute(k, v === true ? '' : String(v));
  }
  for (const c of children.flat()) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return el;
}

const ESC = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
export const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ESC[c]);

/** Lowercase, accent-free, trimmed — used for search. */
export function normalize(s) {
  return String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

export function hashStr(s) {
  let h = 2166136261;
  const str = String(s ?? '');
  for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}

const GRADIENTS = [
  ['#5b43d6', '#1aa7c2'], ['#b8324f', '#c7771a'], ['#1b8fa6', '#23a36f'], ['#a8327a', '#5b43d6'],
  ['#2f7fc4', '#6b45d1'], ['#c4561f', '#b23434'], ['#6f9a1f', '#1a98a6'], ['#9a3fc4', '#4750c9'],
];
/** Deterministic, muted gradient for a game (fallback art background). */
export function gradientFor(seed) {
  const [a, b] = GRADIENTS[hashStr(seed) % GRADIENTS.length];
  return `linear-gradient(135deg, ${a} 0%, ${b} 100%)`;
}

/** Up to two initials, e.g. "Counter-Strike 2" -> "CS". */
export function monogram(name) {
  const words = String(name ?? '').replace(/[^\p{L}\p{N}\s]/gu, ' ').split(/\s+/).filter(Boolean);
  if (!words.length) return '?';
  const letters = words.length === 1 ? words[0].slice(0, 2) : words[0][0] + words[1][0];
  return letters.toUpperCase();
}

export const PLATFORM_COLORS = {
  Steam: '#66c0f4', Epic: '#d4d4d8', Riot: '#ff5c7a', Roblox: '#3ddc84', Minecraft: '#7bc85a',
  'Battle.net': '#3d9bff', EA: '#ff5a5a', Ubisoft: '#6c8cff', GOG: '#b47cff', PC: '#aab2d0',
};
export const platformColor = (p) => PLATFORM_COLORS[p] || PLATFORM_COLORS.PC;

export const SOURCE_LABELS = { folder: 'Pasta de jogos', steam: 'Steam', epic: 'Epic Games' };

/** Duration in pt-BR: "45 min", "12 h 5 min", "320 h". */
export function fmtDuration(sec) {
  const s = Math.max(0, Math.floor(Number(sec) || 0));
  if (s === 0) return '—';
  if (s < 60) return 'menos de 1 min';
  const m = Math.floor(s / 60);
  if (m < 60) return `${m} min`;
  const hrs = Math.floor(m / 60), rest = m % 60;
  if (hrs >= 100 || rest === 0) return `${hrs} h`;
  return `${hrs} h ${rest} min`;
}

const dateFmt = typeof Intl !== 'undefined'
  ? new Intl.DateTimeFormat('pt-BR', { day: 'numeric', month: 'short', year: 'numeric' }) : null;

export function fmtDate(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return dateFmt ? dateFmt.format(d) : d.toDateString();
}

/** Relative time: "agora mesmo", "há 5 min", "ontem", "há 3 dias", then a date. */
export function fmtRelative(iso, now = Date.now()) {
  if (!iso) return 'Nunca';
  const t = new Date(iso).getTime();
  if (Number.isNaN(t)) return 'Nunca';
  const s = Math.max(0, (now - t) / 1000);
  if (s < 60) return 'agora mesmo';
  const m = Math.floor(s / 60);
  if (m < 60) return `há ${m} min`;
  const hr = Math.floor(m / 60);
  if (hr < 24) return hr === 1 ? 'há 1 hora' : `há ${hr} horas`;
  const d = Math.floor(hr / 24);
  if (d === 1) return 'ontem';
  if (d < 7) return `há ${d} dias`;
  if (d < 30) { const w = Math.floor(d / 7); return w === 1 ? 'há 1 semana' : `há ${w} semanas`; }
  return fmtDate(iso);
}

/** Bytes in pt-BR units: "12,4 GB". */
export function fmtBytes(n) {
  const b = Number(n) || 0;
  if (b <= 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.min(units.length - 1, Math.floor(Math.log(b) / Math.log(1024)));
  const v = b / 1024 ** i;
  return `${v.toLocaleString('pt-BR', { maximumFractionDigits: v < 10 && i > 0 ? 1 : 0 })} ${units[i]}`;
}

export const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;

export function debounce(fn, ms) {
  let t = null;
  const wrapped = (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
  wrapped.flush = (...args) => { clearTimeout(t); fn(...args); };
  wrapped.cancel = () => clearTimeout(t);
  return wrapped;
}

export const isTextInput = (el) =>
  !!el && (el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable ||
    (el.tagName === 'INPUT' && !['checkbox', 'radio', 'button', 'range'].includes(el.type)));
