// Sidebar: library sections, platforms (only present ones), collections, add + settings.
import { store, LIBRARY_SECTIONS, platformCounts, collectionCounts, sectionCounts } from '../store.js';
import { icon } from './icons.js';
import { esc, platformColor } from '../util.js';
import * as A from '../actions.js';
import { openAddGame } from '../views/addgame.js';

const SECTION_ICONS = { all: 'library', favorites: 'star', recent: 'clock', hidden: 'eyeOff' };

export function initSidebar(el) {
  let structureKey = '';

  el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-section], [data-sb]');
    if (!b) return;
    if (b.dataset.section) A.goLibrary(b.dataset.section);
    else if (b.dataset.sb === 'add') openAddGame();
    else if (b.dataset.sb === 'settings') A.openSettings();
  });

  const item = (key, label, count, lead) =>
    `<button type="button" class="nav-item" data-nav data-section="${esc(key)}" title="${esc(label)}">` +
    `${lead}<span class="nav-label">${esc(label)}</span><span class="nav-count" data-count="${esc(key)}">${count}</span></button>`;

  function build(games, collections) {
    const sc = sectionCounts(games);
    const plats = platformCounts(games);
    const cols = collectionCounts(games, collections);
    let html = '<nav class="nav" aria-label="Biblioteca"><div class="nav-group"><h2 class="nav-heading">Biblioteca</h2>';
    for (const s of LIBRARY_SECTIONS) html += item(s.key, s.label, sc[s.key], icon(SECTION_ICONS[s.key]));
    html += '</div>';
    if (plats.length) {
      html += '<div class="nav-group"><h2 class="nav-heading">Plataformas</h2>';
      for (const p of plats) {
        html += item(`platform:${p.platform}`, p.platform, p.count,
          `<span class="nav-dot" style="--dot:${platformColor(p.platform)}" aria-hidden="true"><i></i></span>`);
      }
      html += '</div>';
    }
    if (cols.length) {
      html += '<div class="nav-group"><h2 class="nav-heading">Coleções</h2>';
      for (const c of cols) html += item(`collection:${c.name}`, c.name, c.count, icon('tag'));
      html += '</div>';
    }
    html += '</nav><div class="sb-foot">' +
      `<button type="button" class="btn btn-primary sb-add" data-nav data-sb="add" title="Adicionar jogo (Ctrl+N)">${icon('plus')}<span class="nav-label">Adicionar jogo</span></button>` +
      `<button type="button" class="nav-item" data-nav data-sb="settings" title="Configurações (Ctrl+,)">${icon('settings')}<span class="nav-label">Configurações</span></button>` +
      '</div>';
    return html;
  }

  function updateCounts(games, collections) {
    const counts = new Map();
    const sc = sectionCounts(games);
    for (const s of LIBRARY_SECTIONS) counts.set(s.key, sc[s.key]);
    for (const p of platformCounts(games)) counts.set(`platform:${p.platform}`, p.count);
    for (const c of collectionCounts(games, collections)) counts.set(`collection:${c.name}`, c.count);
    for (const n of el.querySelectorAll('[data-count]')) {
      const v = String(counts.get(n.dataset.count) ?? 0);
      if (n.textContent !== v) n.textContent = v;
    }
  }

  return function render(changed) {
    const s = store.state;
    if (changed.has('games') || changed.has('collections') || changed.has('init')) {
      const key = JSON.stringify([platformCounts(s.games).map((p) => p.platform), collectionCounts(s.games, s.collections).map((c) => c.name)]);
      if (key !== structureKey) {
        const focusedKey = document.activeElement?.closest('#sidebar [data-section], #sidebar [data-sb]');
        const sel = focusedKey ? (focusedKey.dataset.section ? `[data-section="${CSS.escape(focusedKey.dataset.section)}"]` : `[data-sb="${focusedKey.dataset.sb}"]`) : null;
        structureKey = key;
        el.innerHTML = build(s.games, s.collections);
        if (sel) el.querySelector(sel)?.focus({ preventScroll: true });
      } else {
        updateCounts(s.games, s.collections);
      }
    }
    // Active section falls back to "Todos" when a platform/collection disappears.
    if (s.section !== 'all' && !el.querySelector(`[data-section="${CSS.escape(s.section)}"]`) && s.status === 'ready') {
      queueMicrotask(() => store.set({ section: 'all' }));
    }
    for (const b of el.querySelectorAll('[data-section]')) {
      const active = s.route.view === 'library' && b.dataset.section === s.section;
      b.classList.toggle('active', active);
      if (active) b.setAttribute('aria-current', 'page'); else b.removeAttribute('aria-current');
    }
    const st = el.querySelector('[data-sb="settings"]');
    if (st) {
      st.classList.toggle('active', s.route.view === 'settings');
      if (s.route.view === 'settings') st.setAttribute('aria-current', 'page'); else st.removeAttribute('aria-current');
    }
    el.classList.toggle('collapsed', !!s.sidebarCollapsed);
  };
}

/** Ordered section keys as shown in the sidebar (for LB/RB cycling). */
export function sidebarSections() {
  return Array.from(document.querySelectorAll('#sidebar [data-section]')).map((b) => b.dataset.section);
}
