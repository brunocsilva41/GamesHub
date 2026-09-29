// Big Picture: full-screen, controller-friendly shelves (Continuar jogando, Favoritos, per platform).
import { store, recentGames, platformCounts, sortGames, filterSection } from '../store.js';
import { createCard, updateCard } from '../components/card.js';
import { reconcile } from '../components/reconcile.js';
import { bindGameEvents } from '../components/cardevents.js';
import { icon } from '../components/icons.js';
import { esc, fmtDuration, fmtRelative, gradientFor, platformColor, debounce } from '../util.js';
import * as A from '../actions.js';

const clockFmt = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' });

export function initBigPicture(root) {
  root.innerHTML = `
    <div class="bp-bg" aria-hidden="true"><img alt=""></div>
    <header class="bp-head">
      <div class="bp-brand"><img src="logo.png" alt="" width="28" height="28"><span>GamesHub</span></div>
      <div class="bp-info" aria-live="polite"><h1 class="bp-name"></h1><div class="bp-meta"></div></div>
      <div class="bp-right"><time class="bp-clock"></time>
        <button type="button" class="btn btn-glass" data-bp="exit">${icon('close')}Sair</button></div>
    </header>
    <div class="bp-shelves" role="region" aria-label="Biblioteca em tela cheia"></div>
    <footer class="bp-hints" aria-hidden="true">
      <span><i class="pad-btn pad-a">A</i><kbd class="kb">Enter</kbd>Jogar</span>
      <span><i class="pad-btn pad-x">X</i><kbd class="kb">I</kbd>Detalhes</span>
      <span><i class="pad-btn pad-y">Y</i><kbd class="kb">F</kbd>Favoritar</span>
      <span><i class="pad-btn pad-b">B</i><kbd class="kb">Esc</kbd>Sair</span>
    </footer>`;

  const shelves = root.querySelector('.bp-shelves');
  const bgImg = root.querySelector('.bp-bg img');
  const clock = root.querySelector('.bp-clock');
  let clockTimer = null;
  let shownId = null;

  bindGameEvents(shelves);
  root.querySelector('[data-bp="exit"]').addEventListener('click', () => A.toggleBigPicture(false));

  const showGame = debounce((id) => {
    const g = store.get(id);
    if (!g) return;
    root.querySelector('.bp-name').textContent = g.name;
    root.querySelector('.bp-meta').innerHTML =
      `<span class="chip"><i class="dot" style="--dot:${platformColor(g.platform)}"></i>${esc(g.platform)}</span>` +
      (g.running ? '<span class="chip chip-live"><i class="pulse"></i>Em execução</span>' : '') +
      `<span class="chip">${g.playSeconds ? fmtDuration(g.playSeconds) : 'Nunca jogado'}</span>` +
      (g.lastPlayed ? `<span class="chip">Jogado ${fmtRelative(g.lastPlayed)}</span>` : '');
    if (shownId === id) return;
    shownId = id;
    const a = g.art || {};
    const url = a.hero || a.header || a.capsule;
    root.style.setProperty('--bp-fallback', gradientFor(g.id));
    bgImg.classList.remove('loaded');
    if (url) {
      bgImg.onload = () => bgImg.classList.add('loaded');
      bgImg.src = url;
    } else bgImg.removeAttribute('src');
  }, 90);

  shelves.addEventListener('focusin', (e) => {
    const card = e.target.closest('[data-game]');
    if (!card) return;
    showGame(card.dataset.game);
    const reduce = document.body.classList.contains('reduce-motion');
    card.scrollIntoView({ block: 'center', inline: 'center', behavior: reduce ? 'auto' : 'smooth' });
  });

  function buildShelves() {
    const games = store.state.games;
    const list = [];
    const recent = recentGames(games, 12);
    if (recent.length) list.push({ key: 'recent', title: 'Continuar jogando', games: recent });
    const favs = sortGames(filterSection(games, 'favorites'), 'name');
    if (favs.length) list.push({ key: 'fav', title: 'Favoritos', games: favs });
    for (const p of platformCounts(games)) {
      list.push({ key: `p:${p.platform}`, title: p.platform, games: sortGames(filterSection(games, `platform:${p.platform}`), 'name') });
    }
    return list;
  }

  const ctx = { style: 'landscape' };
  function renderShelves() {
    const list = buildShelves();
    reconcile(shelves, list, {
      key: (s) => s.key,
      create: (s) => {
        const el = document.createElement('section');
        el.className = 'bp-shelf';
        el.innerHTML = `<h2 class="bp-shelf-title"></h2><div class="bp-row" role="list"></div>`;
        el.querySelector('h2').textContent = s.title;
        fill(el, s);
        return el;
      },
      update: (el, s) => fill(el, s),
    });
    if (!list.length) shelves.innerHTML = '<p class="bp-empty">Nenhum jogo na biblioteca ainda.</p>';
  }
  const fill = (el, s) => reconcile(el.querySelector('.bp-row'), s.games, {
    key: (g) => g.id, create: (g) => createCard(g, ctx), update: (c, g) => updateCard(c, g, ctx),
  });

  const tick = () => { clock.textContent = clockFmt.format(new Date()); };

  return function render(changed) {
    const s = store.state;
    if (changed.has('bigPicture') || changed.has('init')) {
      root.hidden = !s.bigPicture;
      document.body.classList.toggle('bp-on', s.bigPicture);
      clearInterval(clockTimer);
      if (s.bigPicture) {
        tick();
        clockTimer = setInterval(tick, 15000);
        shelves.querySelector('.bp-empty')?.remove();
        renderShelves();
        requestAnimationFrame(() => {
          const first = shelves.querySelector('[data-nav]');
          if (first) first.focus({ preventScroll: true });
          else root.querySelector('[data-bp="exit"]').focus();
          // focus() doesn't fire focusin while the window is inactive: show the first game explicitly.
          const firstGame = shelves.querySelector('[data-game]');
          if (firstGame) showGame(firstGame.dataset.game);
        });
      }
      return;
    }
    if (s.bigPicture && changed.has('games')) {
      shelves.querySelector('.bp-empty')?.remove();
      renderShelves();
      if (shownId) showGame(shownId);
    }
  };
}
