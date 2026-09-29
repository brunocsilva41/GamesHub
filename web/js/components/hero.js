// Hero banner: art.hero background (fallback header/capsule -> gradient) + art.logo overlay (fallback title).
import { gradientFor, platformColor, fmtDuration, fmtRelative, esc } from '../util.js';
import { icon } from './icons.js';

export function createHero(extraClass = '') {
  const el = document.createElement('section');
  el.className = `hero ${extraClass}`.trim();
  el.innerHTML =
    '<div class="hero-bg" aria-hidden="true"><img class="hero-img" alt="" decoding="async"></div>' +
    '<div class="hero-shade" aria-hidden="true"></div>' +
    '<div class="hero-content">' +
      '<span class="eyebrow"></span>' +
      '<div class="hero-brand"><img class="hero-logo" alt="" decoding="async" hidden><h2 class="hero-title"></h2></div>' +
      '<div class="hero-meta"></div>' +
      '<div class="hero-actions"></div>' +
    '</div>';
  return el;
}

function loadChain(img, urls, onDone) {
  const token = (img._token = (img._token || 0) + 1);
  const next = (i) => {
    if (img._token !== token) return;
    if (i >= urls.length) { onDone(null); return; }
    img.onload = () => { if (img._token === token) onDone(urls[i]); };
    img.onerror = () => next(i + 1);
    img.src = urls[i];
  };
  next(0);
}

/** opts: { eyebrow, actionsHtml } — actionsHtml is only replaced when it changes. */
export function updateHero(el, g, opts = {}) {
  if (!g) return;
  const a = g.art || {};
  el.dataset.game = g.id;
  el.style.setProperty('--hero-fallback', gradientFor(g.id));

  const bgKey = JSON.stringify([g.id, a.hero, a.header, a.capsule]);
  if (el._bgKey !== bgKey) {
    el._bgKey = bgKey;
    const img = el.querySelector('.hero-img');
    img.classList.remove('loaded');
    el.classList.remove('has-bg', 'bg-soft');
    const urls = [a.hero, a.header, a.capsule].filter(Boolean);
    loadChain(img, urls, (url) => {
      if (!url) { img.removeAttribute('src'); return; }
      el.classList.add('has-bg');
      el.classList.toggle('bg-soft', url !== a.hero); // non-hero art is blurred to hide low resolution
      requestAnimationFrame(() => img.classList.add('loaded'));
    });
  }

  const title = el.querySelector('.hero-title');
  title.textContent = g.name;
  const logoKey = JSON.stringify([g.id, a.logo]);
  if (el._logoKey !== logoKey) {
    el._logoKey = logoKey;
    const logo = el.querySelector('.hero-logo');
    logo.hidden = true;
    title.classList.remove('sr-only');
    logo.alt = g.name;
    if (a.logo) {
      loadChain(logo, [a.logo], (url) => {
        if (!url) return;
        logo.hidden = false;
        title.classList.add('sr-only');
      });
    } else logo.removeAttribute('src');
  }

  el.querySelector('.eyebrow').textContent = opts.eyebrow ?? '';
  el.querySelector('.eyebrow').hidden = !opts.eyebrow;
  const meta = [
    `<span class="chip"><i class="dot" style="--dot:${platformColor(g.platform)}"></i>${esc(g.platform)}</span>`,
  ];
  if (g.running) meta.push(`<span class="chip chip-live"><i class="pulse"></i>Em execução</span>`);
  if (g.updatePending) meta.push('<span class="chip chip-warn">Atualização pendente</span>');
  if (g.broken) meta.push(`<span class="chip chip-err" title="${esc(g.brokenReason || '')}">Atalho quebrado</span>`);
  if (g.playSeconds) meta.push(`<span class="chip">${icon('clock')}${fmtDuration(g.playSeconds)}</span>`);
  meta.push(`<span class="chip">${g.lastPlayed ? `Jogado ${fmtRelative(g.lastPlayed)}` : 'Nunca jogado'}</span>`);
  const metaHtml = meta.join('');
  const metaEl = el.querySelector('.hero-meta');
  if (metaEl._html !== metaHtml) { metaEl._html = metaHtml; metaEl.innerHTML = metaHtml; }

  if (opts.actionsHtml !== undefined) {
    const act = el.querySelector('.hero-actions');
    if (act._html !== opts.actionsHtml) {
      const focused = act.contains(document.activeElement) ? document.activeElement.dataset.hero : null;
      act._html = opts.actionsHtml;
      act.innerHTML = opts.actionsHtml;
      if (focused) act.querySelector(`[data-hero="${focused}"]`)?.focus({ preventScroll: true });
    }
  }
}

/** Play button; becomes a "Jogar ▾" split button when the game has launch variants. */
export function playButtonHtml(g, extra = '') {
  if (g.running) {
    return `<button type="button" class="btn btn-live btn-lg" data-nav data-hero="play" ${extra} aria-disabled="true"><i class="pulse"></i>Em execução</button>`;
  }
  const play = `<button type="button" class="btn btn-primary btn-lg" data-nav data-hero="play" ${extra}>${icon('play')}Jogar</button>`;
  const hasVariants = Array.isArray(g.variants) && g.variants.length > 0;
  if (!hasVariants) return play;
  return `<span class="split">${play}<button type="button" class="btn btn-primary btn-lg split-caret" data-nav data-hero="variants" aria-haspopup="menu" aria-label="Escolher versão para jogar" title="Escolher versão">${icon('chevronDown')}</button></span>`;
}
