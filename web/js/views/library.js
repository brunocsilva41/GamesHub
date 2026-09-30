// Library view: hero, "Continuar jogando" shelf, notices, toolbar (search/genre/sort/view/card style),
// quick filters, keyed grid/list. The "size" sort is client-side only (settings.sortBy enum), kept in localStorage.
import { store, SORTS, SERVER_SORTS, QUICK_FILTERS, effectiveSort, sectionLabel, pickFeatured, recentGames, genreList } from '../store.js';
import { initLibraryNotices } from './libnotices.js';
import { createCard, updateCard, createRow, updateRow } from '../components/card.js';
import { reconcile } from '../components/reconcile.js';
import { createHero, updateHero, playButtonHtml } from '../components/hero.js';
import { bindGameEvents } from '../components/cardevents.js';
import { enhanceHScroll } from '../components/hscroll.js';
import { icon } from '../components/icons.js';
import { esc, plural } from '../util.js';
import { renderLibraryState } from './states.js';
import * as A from '../actions.js';

export function initLibrary(root) {
  root.innerHTML = `
    <div class="lib-top">
      <div class="hero-slot"></div>
      <section class="shelf" aria-labelledby="shelf-continue" hidden>
        <header class="section-head"><h2 id="shelf-continue">Continuar jogando</h2></header>
        <div class="shelf-row" role="list"></div>
      </section>
    </div>
    <div class="lib-notices" hidden></div>
    <div class="toolbar" role="toolbar" aria-label="Opções da biblioteca">
      <div class="toolbar-title"><h1 class="lib-title">Todos</h1><span class="lib-count"></span></div>
      <label class="search">
        ${icon('search')}
        <input id="search" type="search" placeholder="Buscar jogos" autocomplete="off" spellcheck="false" aria-label="Buscar jogos">
        <kbd>Ctrl F</kbd>
      </label>
      <label class="select-wrap" title="Filtrar por gênero" data-genre-wrap hidden>
        <span class="sr-only">Gênero</span>
        <select class="select" data-tool="genre"></select>
        ${icon('chevronDown')}
      </label>
      <label class="select-wrap" title="Ordenar por">
        <span class="sr-only">Ordenar por</span>
        <select class="select" data-tool="sort">${SORTS.map((s) => `<option value="${s.key}">${esc(s.label)}</option>`).join('')}</select>
        ${icon('chevronDown')}
      </label>
      <div class="segmented" role="group" aria-label="Modo de exibição">
        <button type="button" data-tool="view" data-value="grid" aria-label="Grade" title="Grade">${icon('grid')}</button>
        <button type="button" data-tool="view" data-value="list" aria-label="Lista" title="Lista">${icon('list')}</button>
      </div>
      <div class="segmented" role="group" aria-label="Estilo dos cards" data-style-group>
        <button type="button" data-tool="style" data-value="landscape" aria-label="Paisagem" title="Cards em paisagem">${icon('landscape')}</button>
        <button type="button" data-tool="style" data-value="portrait" aria-label="Retrato" title="Cards em retrato">${icon('portrait')}</button>
      </div>
    </div>
    <div class="quick-filters" role="group" aria-label="Filtros rápidos">${QUICK_FILTERS.map((f) =>
      `<button type="button" class="chip-toggle" data-nav data-quick="${f.key}" aria-pressed="false">${esc(f.label)}</button>`).join('')}</div>
    <div class="games grid" role="list" aria-label="Jogos"></div>
    <div class="lib-state" hidden></div>`;

  const q = (s) => root.querySelector(s);
  const heroSlot = q('.hero-slot');
  const hero = createHero('hero-library');
  heroSlot.append(hero);
  const shelf = q('.shelf');
  const shelfRow = q('.shelf-row');
  const gamesEl = q('.games');
  const stateEl = q('.lib-state');
  const search = q('#search');
  let layoutKey = '';
  let scrolledReady = false;
  const renderNotices = initLibraryNotices(q('.lib-notices'));
  try { if (localStorage.getItem('gh.clientSort') === 'size') store.set({ clientSort: 'size' }); } catch { /* storage unavailable */ }

  bindGameEvents(gamesEl);
  bindGameEvents(shelfRow);
  enhanceHScroll(shelfRow, { controls: shelf.querySelector('.section-head'), label: 'jogos recentes' });
  hero.addEventListener('click', (e) => {
    const b = e.target.closest('[data-hero]');
    if (!b) return;
    const id = hero.dataset.game;
    if (b.dataset.hero === 'play') A.launch(id);
    else if (b.dataset.hero === 'details') A.openDetails(id);
    else if (b.dataset.hero === 'variants') A.openVariantMenu(id, b);
  });

  search.addEventListener('input', () => store.set({ query: search.value }));
  search.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown' || (e.key === 'Enter' && search.value)) {
      const first = gamesEl.querySelector('[data-nav]');
      if (first) { e.preventDefault(); first.focus(); }
    } else if (e.key === 'Escape') {
      e.preventDefault(); e.stopPropagation();
      if (search.value) { search.value = ''; store.set({ query: '' }); } else search.blur();
    }
  });
  root.addEventListener('change', (e) => {
    if (e.target.matches('[data-tool="sort"]')) {
      const v = e.target.value;
      const client = SERVER_SORTS.includes(v) ? '' : v;
      store.set({ clientSort: client });
      try { if (client) localStorage.setItem('gh.clientSort', client); else localStorage.removeItem('gh.clientSort'); } catch { /* storage unavailable */ }
      if (!client) A.saveSettings({ sortBy: v });
    }
    else if (e.target.matches('[data-tool="genre"]')) store.set({ genre: e.target.value });
  });
  root.addEventListener('click', (e) => {
    const qf = e.target.closest('[data-quick]');
    if (qf) { store.set({ quick: store.state.quick === qf.dataset.quick ? '' : qf.dataset.quick }); return; }
    const b = e.target.closest('[data-tool]');
    if (!b || b.tagName === 'SELECT') return;
    if (b.dataset.tool === 'view') A.saveSettings({ view: b.dataset.value });
    else if (b.dataset.tool === 'style') A.saveSettings({ cardStyle: b.dataset.value });
  });
  stateEl.addEventListener('click', (e) => {
    const b = e.target.closest('[data-state-action]');
    if (!b) return;
    const act = b.dataset.stateAction;
    if (act === 'clear') { search.value = ''; store.set({ query: '' }); search.focus(); }
    else if (act === 'all') A.goLibrary('all');
    else if (act === 'add') import('./addgame.js').then((m) => m.openAddGame());
    else if (act === 'settings') A.openSettings();
    else if (act === 'rescan') A.rescan();
    else if (act === 'genre-clear') store.set({ genre: '' });
    else if (act === 'quick-clear') store.set({ quick: '' });
  });

  let genreKey = '';
  function renderGenres() {
    const s = store.state;
    const genres = genreList(s.games);
    if (s.genre && !genres.includes(s.genre)) { queueMicrotask(() => store.set({ genre: '' })); }
    const key = JSON.stringify(genres);
    const sel = q('[data-tool="genre"]');
    if (key !== genreKey) {
      genreKey = key;
      sel.innerHTML = '<option value="">Todos os gêneros</option>' + genres.map((g) => `<option value="${esc(g)}">${esc(g)}</option>`).join('');
    }
    q('[data-genre-wrap]').hidden = genres.length === 0;
    if (sel.value !== s.genre) sel.value = s.genre;
  }

  function renderGrid(list) {
    const s = store.state.settings;
    const mode = s.view === 'list' ? 'list' : 'grid';
    const style = s.cardStyle === 'portrait' ? 'portrait' : 'landscape';
    const key = `${mode}|${style}`;
    if (key !== layoutKey) {
      layoutKey = key;
      gamesEl.textContent = ''; // explicit layout change: rebuild once
      gamesEl.className = `games ${mode}${mode === 'grid' ? ` grid-${style}` : ''}`;
    }
    const ctx = { style };
    reconcile(gamesEl, list, mode === 'list'
      ? { key: (g) => g.id, create: (g) => createRow(g), update: (el, g) => updateRow(el, g) }
      : { key: (g) => g.id, create: (g) => createCard(g, ctx), update: (el, g) => updateCard(el, g, ctx) });
  }

  function renderTop(showTop) {
    const s = store.state;
    const featured = showTop ? pickFeatured(s.games) : null;
    hero.hidden = !featured;
    if (featured) {
      const eyebrow = featured.running ? 'Em execução' : featured.lastPlayed ? 'Continuar jogando' : 'Em destaque';
      updateHero(hero, featured, {
        eyebrow,
        actionsHtml: playButtonHtml(featured) +
          `<button type="button" class="btn btn-glass btn-lg" data-nav data-hero="details">${icon('info')}Detalhes</button>`,
      });
    }
    const recent = showTop ? recentGames(s.games, 10) : [];
    shelf.hidden = recent.length < 2;
    const ctx = { style: 'landscape' };
    if (!shelf.hidden) reconcile(shelfRow, recent, { key: (g) => g.id, create: (g) => createCard(g, ctx), update: (el, g) => updateCard(el, g, ctx) });
  }

  return function render(changed) {
    const s = store.state;
    renderNotices(changed);
    const relevant = ['games', 'section', 'query', 'genre', 'quick', 'clientSort', 'settings', 'status', 'init'].some((k) => changed.has(k));
    if (!relevant) return;

    // Toolbar
    q('.lib-title').textContent = sectionLabel(s.section);
    const sortSel = q('[data-tool="sort"]');
    if (sortSel.value !== effectiveSort(s)) sortSel.value = effectiveSort(s);
    for (const b of root.querySelectorAll('[data-quick]')) {
      const on = s.quick === b.dataset.quick;
      b.classList.toggle('on', on);
      b.setAttribute('aria-pressed', String(on));
    }
    sortSel.disabled = s.section === 'recent';
    for (const b of root.querySelectorAll('[data-tool="view"]')) b.setAttribute('aria-pressed', String(b.dataset.value === s.settings.view));
    for (const b of root.querySelectorAll('[data-tool="style"]')) b.setAttribute('aria-pressed', String(b.dataset.value === s.settings.cardStyle));
    q('[data-style-group]').hidden = s.settings.view === 'list';
    if (search.value !== s.query && document.activeElement !== search) search.value = s.query;
    renderGenres();

    if (s.status === 'loading') {
      hero.hidden = true; shelf.hidden = true;
      q('.lib-count').textContent = '';
      gamesEl.className = 'games grid grid-landscape skeleton-grid';
      gamesEl.innerHTML = Array.from({ length: 12 }, () => '<div class="card sk"><span class="art art-landscape"></span><span class="sk-line"></span><span class="sk-line short"></span></div>').join('');
      layoutKey = '';
      stateEl.hidden = true;
      return;
    }
    if (gamesEl.classList.contains('skeleton-grid')) { gamesEl.textContent = ''; layoutKey = ''; }

    const list = store.visible();
    q('.lib-count').textContent = plural(list.length, 'jogo', 'jogos');
    renderTop(s.section === 'all' && !s.query.trim() && !s.genre && !s.quick && s.games.length > 0);
    renderGrid(list);

    const st = renderLibraryState(s, list.length);
    stateEl.hidden = !st;
    if (st && stateEl._html !== st) { stateEl._html = st; stateEl.innerHTML = st; }
    if (!st) stateEl._html = '';
    q('.toolbar').hidden = s.games.length === 0;
    q('.quick-filters').hidden = s.games.length === 0;
    // First real render: start at the top (no stale offset from the skeleton or restored scroll).
    if (!scrolledReady && s.status === 'ready') {
      scrolledReady = true;
      root.scrollTop = 0;
      requestAnimationFrame(() => { root.scrollTop = 0; });
    }

    if (changed.has('section')) root.scrollTop = 0;
  };
}
