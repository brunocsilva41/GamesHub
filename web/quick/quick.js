// Quick-launch palette page. Thin view: search/ranking happens in C# (QuickSearch); see README.md.
const $ = (id) => document.getElementById(id);
const input = $('q');
const list = $('results');
const empty = $('empty');
const section = $('section');
const statusEl = $('status');
const palette = document.querySelector('.palette');

const host = window.chrome && window.chrome.webview;
let seq = 0;            // last query sequence sent
let items = [];         // current results
let active = -1;        // index of the highlighted option
let statusTimer = 0;
let mockGames = [];     // dev mode only

const rtf = new Intl.RelativeTimeFormat('pt-BR', { numeric: 'auto' });      // "ontem"
const rtfN = new Intl.RelativeTimeFormat('pt-BR', { numeric: 'always' });   // "há 1 semana"

function send(msg) {
  if (host) host.postMessage(msg);
  else mock(msg);
}

// ------------------------------------------------------------------ rendering

function lastPlayedText(iso) {
  if (!iso) return 'Nunca jogado';
  const then = new Date(iso);
  if (isNaN(then)) return '';
  const days = Math.round((startOfDay(new Date()) - startOfDay(then)) / 86400000);
  if (days <= 0) return 'Jogado hoje';
  if (days < 7) return 'Jogado ' + rtf.format(-days, 'day');
  if (days < 30) return 'Jogado ' + rtfN.format(-Math.floor(days / 7), 'week');
  if (days < 365) return 'Jogado ' + rtfN.format(-Math.floor(days / 30), 'month');
  return 'Jogado ' + rtfN.format(-Math.floor(days / 365), 'year');
}
function startOfDay(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime(); }

function playTimeText(sec) {
  if (!sec || sec < 60) return '';
  const h = sec / 3600;
  if (h < 1) return Math.round(sec / 60) + ' min';
  return (h < 10 ? h.toFixed(1).replace('.', ',') : Math.round(h).toLocaleString('pt-BR')) + ' h';
}

/** Name with <mark> around highlighted [start, length] ranges (DOM nodes, no innerHTML). */
function nameNode(name, hl) {
  const span = document.createElement('span');
  span.className = 'name';
  let pos = 0;
  for (const [start, len] of hl || []) {
    if (start < pos || start >= name.length) continue;
    if (start > pos) span.append(name.slice(pos, start));
    const m = document.createElement('mark');
    m.textContent = name.slice(start, start + len);
    span.append(m);
    pos = start + len;
  }
  if (pos < name.length) span.append(name.slice(pos));
  return span;
}

function thumbNode(g) {
  const t = document.createElement('span');
  t.className = 'thumb';
  const letter = () => {
    t.textContent = '';
    const l = document.createElement('span');
    l.className = 'letter';
    l.textContent = (g.name || '?').trim().charAt(0).toUpperCase();
    t.append(l);
  };
  const src = g.header || g.icon;
  if (!src) { letter(); return t; }
  const img = document.createElement('img');
  img.alt = '';
  img.decoding = 'async';
  img.className = g.header ? 'header' : 'icon';
  img.src = src;
  img.onerror = () => {
    if (g.header && g.icon && img.src !== g.icon) { img.className = 'icon'; img.src = g.icon; }
    else letter();
  };
  t.append(img);
  return t;
}

function metaNode(g) {
  const m = document.createElement('span');
  m.className = 'meta';
  const parts = [];
  if (g.running) {
    const live = document.createElement('span');
    live.className = 'live';
    live.textContent = 'Em execução';
    parts.push(live);
  }
  if (g.platform) parts.push(g.platform);
  if (!g.running) parts.push(lastPlayedText(g.lastPlayed));
  const pt = playTimeText(g.playSeconds);
  if (pt) parts.push(pt);
  if (g.favorite) {
    const f = document.createElement('span');
    f.className = 'fav';
    f.textContent = '★';
    f.setAttribute('aria-label', 'Favorito');
    parts.push(f);
  }
  if (g.hidden) {
    const h = document.createElement('span');
    h.className = 'hiddenTag';
    h.textContent = 'oculto';
    parts.push(h);
  }
  parts.filter(Boolean).forEach((p, i) => {
    if (i) { const s = document.createElement('span'); s.className = 'sep'; s.textContent = '·'; s.setAttribute('aria-hidden', 'true'); m.append(s); }
    m.append(p);
  });
  return m;
}

function render(newItems, label) {
  items = Array.isArray(newItems) ? newItems : [];
  section.textContent = label;
  const frag = document.createDocumentFragment();
  items.forEach((g, i) => {
    const li = document.createElement('li');
    li.className = 'item';
    li.id = 'opt-' + i;
    li.setAttribute('role', 'option');
    li.setAttribute('aria-selected', 'false');
    li.dataset.index = i;
    const text = document.createElement('span');
    text.className = 'text';
    text.append(nameNode(g.name || '', g.hl), metaNode(g));
    const go = document.createElement('span');
    go.className = 'go';
    go.setAttribute('aria-hidden', 'true');
    go.textContent = g.running ? 'Em execução' : 'Jogar ↵';
    li.append(thumbNode(g), text, go);
    frag.append(li);
  });
  list.replaceChildren(frag);
  empty.hidden = items.length > 0 || input.value.trim() === '';
  list.hidden = items.length === 0;
  setActive(items.length ? 0 : -1);
}

function setActive(i) {
  const prev = list.querySelector('[aria-selected="true"]');
  if (prev) prev.setAttribute('aria-selected', 'false');
  active = i;
  if (i < 0 || i >= items.length) { input.setAttribute('aria-activedescendant', ''); return; }
  const el = $('opt-' + i);
  el.setAttribute('aria-selected', 'true');
  input.setAttribute('aria-activedescendant', el.id);
  el.scrollIntoView({ block: 'nearest' });
}

function move(delta, wrap = true) {
  if (!items.length) return;
  let i = active + delta;
  if (wrap) i = (i + items.length) % items.length;
  else i = Math.max(0, Math.min(items.length - 1, i));
  setActive(i);
}

function setStatus(text, kind = 'info', ms = 4000) {
  clearTimeout(statusTimer);
  statusEl.textContent = text || '';
  statusEl.className = 'status ' + kind;
  statusEl.parentElement.classList.toggle('has-status', !!text);
  if (text && ms) statusTimer = setTimeout(() => setStatus(''), ms);
}

function query() {
  send({ type: 'query', seq: ++seq, q: input.value });
}

function launch(i, reveal) {
  const g = items[i];
  if (!g) return;
  send({ type: reveal ? 'reveal' : 'launch', id: g.id });
}

function resetView(msg, focus) {
  seq++;                       // drop in-flight results
  input.value = '';
  render(msg.items, 'Recentes');
  setStatus('');
  document.querySelectorAll('.item.busy').forEach((e) => e.classList.remove('busy'));
  if ('reduceMotion' in msg) document.documentElement.classList.toggle('reduce-motion', !!msg.reduceMotion);
  if (focus) {
    input.focus();
    input.select();
    palette.classList.remove('appear');
    void palette.offsetWidth;  // restart the entrance animation
    palette.classList.add('appear');
  }
}

// ------------------------------------------------------------------ messages from C#

function onMessage(msg) {
  if (!msg || typeof msg !== 'object') return;
  switch (msg.type) {
    case 'show': resetView(msg, true); break;
    case 'reset': resetView(msg, false); break;
    case 'results':
      if (msg.seq !== seq) return; // stale
      render(msg.items, input.value.trim() ? 'Resultados' : 'Recentes');
      break;
    case 'changed': query(); break;
    case 'launching': {
      const i = items.findIndex((g) => g.id === msg.id);
      if (i >= 0) {
        const el = $('opt-' + i);
        el.classList.add('busy');
        el.querySelector('.go').textContent = 'Iniciando…';
      }
      setStatus('Iniciando…', 'info', 0);
      break;
    }
    case 'status':
      document.querySelectorAll('.item.busy').forEach((e) => {
        e.classList.remove('busy');
        e.querySelector('.go').textContent = 'Jogar ↵';
      });
      setStatus(msg.text, msg.kind === 'err' ? 'err' : 'info', 6000);
      break;
  }
}

// ------------------------------------------------------------------ input

input.addEventListener('input', query);

input.addEventListener('keydown', (e) => {
  switch (e.key) {
    case 'ArrowDown': move(1); break;
    case 'ArrowUp': move(-1); break;
    case 'PageDown': move(5, false); break;
    case 'PageUp': move(-5, false); break;
    case 'Tab': move(e.shiftKey ? -1 : 1); break;
    case 'Enter': if (active >= 0) launch(active, e.ctrlKey); break;
    case 'Escape': send({ type: 'hide' }); break;
    default: return;
  }
  e.preventDefault();
});

// Keep focus in the search box (clicks on the list or background).
document.addEventListener('mousedown', (e) => { if (e.target !== input) e.preventDefault(); });

list.addEventListener('mousemove', (e) => {
  const li = e.target.closest('.item');
  if (li && +li.dataset.index !== active) setActive(+li.dataset.index);
});
list.addEventListener('click', (e) => {
  const li = e.target.closest('.item');
  if (li) launch(+li.dataset.index, e.ctrlKey);
});
window.addEventListener('focus', () => input.focus());

if (host) {
  host.addEventListener('message', (e) => onMessage(e.data));
  window.addEventListener('error', (e) => send({ type: 'log', level: 'error', msg: String(e.message) }));
  send({ type: 'ready' });
} else {
  mockInit();
}

// ------------------------------------------------------------------ dev mode (page opened in a browser)
// Minimal stand-in so the page can be styled standalone. Not used inside the app.

function mockInit() {
  const d = (n) => new Date(Date.now() - n * 86400000).toISOString();
  mockGames = [
    { id: 'steam:1', name: 'LEGO Marvel Super Heroes', platform: 'Steam', lastPlayed: d(2), playSeconds: 41000 },
    { id: 'steam:2', name: 'Counter-Strike 2', platform: 'Steam', lastPlayed: d(0), playSeconds: 900000, running: true, favorite: true },
    { id: 'epic:3', name: 'Fortnite', platform: 'Epic', lastPlayed: d(12), playSeconds: 12000 },
    { id: 'folder:4', name: 'Pokémon Edição Ouro', platform: 'PC', lastPlayed: null, playSeconds: 0 },
    { id: 'riot:5', name: 'VALORANT', platform: 'Riot', lastPlayed: d(40), playSeconds: 350000 },
  ].map((g) => ({ collections: [], favorite: false, running: false, hidden: false, header: null, icon: null, hl: [], ...g }));
  onMessage({ type: 'show', items: mockGames });
}
function mock(msg) {
  if (msg.type === 'query') {
    const fold = (s) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    const q = fold(msg.q.trim());
    const res = mockGames.filter((g) => fold(g.name).includes(q)).map((g) => {
      const at = fold(g.name).indexOf(q);
      return { ...g, hl: q ? [[at, q.length]] : [] };
    });
    setTimeout(() => onMessage({ type: 'results', seq: msg.seq, items: res }), 0);
  } else if (msg.type === 'launch' || msg.type === 'reveal') {
    onMessage({ type: 'status', kind: 'info', text: 'Modo demonstração: ' + msg.type + ' ' + msg.id });
  }
}
