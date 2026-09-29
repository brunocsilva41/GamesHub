// Game card (grid) and row (list). create*() builds once; update*() patches only what changed,
// so `games` events never rebuild the grid or restart animations.
import { artBox, setArt, cardSources } from './art.js';
import { icon } from './icons.js';
import { fmtDuration, fmtRelative, platformColor } from '../util.js';

const subText = (g) => {
  if (g.running) return 'Em execução';
  if (!g.playSeconds && !g.lastPlayed) return 'Nunca jogado';
  const parts = [];
  if (g.playSeconds) parts.push(fmtDuration(g.playSeconds));
  if (g.lastPlayed) parts.push(fmtRelative(g.lastPlayed));
  return parts.join(' · ');
};

// Status badges (wave-2 fields are optional: updatePending, broken/brokenReason).
const FLAGS = '<span class="badge badge-live" hidden><i class="pulse"></i>Em execução</span>' +
  '<span class="badge badge-warn" data-flag="update" hidden>Atualização pendente</span>' +
  '<span class="badge badge-err" data-flag="broken" hidden>Atalho quebrado</span>';

function setFlags(el, g, prev) {
  if (prev.running !== g.running) {
    el.classList.toggle('is-running', !!g.running);
    el.querySelector('.badge-live').hidden = !g.running;
  }
  if (prev.updatePending !== g.updatePending) el.querySelector('[data-flag="update"]').hidden = !g.updatePending;
  if (prev.broken !== g.broken || prev.brokenReason !== g.brokenReason) {
    const b = el.querySelector('[data-flag="broken"]');
    b.hidden = !g.broken;
    b.title = g.brokenReason || '';
    el.classList.toggle('is-broken', !!g.broken);
  }
}

function favButton() {
  return `<button type="button" class="card-fav" tabindex="-1" data-action="fav">${icon('star', 'off')}${icon('starFill', 'on')}</button>`;
}

export function createCard(g, ctx) {
  const el = document.createElement('div');
  el.className = 'card';
  el.dataset.game = g.id;
  el.setAttribute('role', 'listitem');
  el.innerHTML =
    `<button type="button" class="card-hit" data-nav data-action="open"></button>` +
    `<span class="card-over">` +
      `<span class="badge-flags">${FLAGS}</span>` +
      `<span class="badge badge-platform"><i class="dot"></i><b></b></span>` +
      favButton() +
      `<button type="button" class="card-info" tabindex="-1" data-action="details" aria-label="Detalhes">${icon('info')}</button>` +
    `</span>`;
  const hit = el.firstElementChild;
  hit.append(artBox(ctx.style === 'portrait' ? 'art-portrait' : 'art-landscape'));
  hit.insertAdjacentHTML('beforeend', '<span class="card-meta"><span class="card-title"></span><span class="card-sub"></span></span>');
  updateCard(el, g, ctx);
  return el;
}

export function updateCard(el, g, ctx) {
  const prev = el._g || {};
  el._g = g;
  if (prev === g) return;
  const hit = el.querySelector('.card-hit');
  if (prev.name !== g.name || prev.platform !== g.platform || prev.running !== g.running) {
    hit.setAttribute('aria-label', `${g.name} — ${g.platform}${g.running ? ', em execução' : ''}. Enter para jogar.`);
    el.querySelector('.card-title').textContent = g.name;
    el.title = g.name;
  }
  if (prev.platform !== g.platform) {
    const b = el.querySelector('.badge-platform');
    b.querySelector('b').textContent = g.platform;
    b.style.setProperty('--dot', platformColor(g.platform));
  }
  setFlags(el, g, prev);
  if (prev.favorite !== g.favorite || prev.name !== g.name) setFav(el, g);
  el.classList.toggle('is-hidden', !!g.hidden);
  const sub = subText(g);
  const subEl = el.querySelector('.card-sub');
  if (subEl.textContent !== sub) subEl.textContent = sub;
  if (prev.art !== g.art || prev.name !== g.name) {
    setArt(el.querySelector('.art'), {
      sources: cardSources(g.art, ctx.style), icon: g.art?.icon, name: g.name, seed: g.id,
    });
  }
}

function setFav(el, g) {
  el.classList.toggle('is-fav', !!g.favorite);
  const b = el.querySelector('.card-fav');
  b.setAttribute('aria-pressed', g.favorite ? 'true' : 'false');
  b.setAttribute('aria-label', g.favorite ? `Remover ${g.name} dos favoritos` : `Favoritar ${g.name}`);
  b.title = g.favorite ? 'Remover dos favoritos (F)' : 'Favoritar (F)';
}

export function createRow(g) {
  const el = document.createElement('div');
  el.className = 'row';
  el.dataset.game = g.id;
  el.setAttribute('role', 'listitem');
  el.innerHTML =
    `<button type="button" class="row-hit" data-nav data-action="open">` +
      `<span class="row-name"><span class="row-title"></span>${FLAGS}</span>` +
      `<span class="row-platform"><i class="dot"></i><span></span></span>` +
      `<span class="row-last"></span><span class="row-time"></span>` +
    `</button>` + favButton();
  el.firstElementChild.prepend(artBox('art-square'));
  updateRow(el, g);
  return el;
}

export function updateRow(el, g) {
  const prev = el._g || {};
  el._g = g;
  if (prev === g) return;
  const q = (s) => el.querySelector(s);
  if (prev.name !== g.name) { q('.row-title').textContent = g.name; el.title = g.name; }
  if (prev.name !== g.name || prev.running !== g.running) {
    q('.row-hit').setAttribute('aria-label', `${g.name} — ${g.platform}${g.running ? ', em execução' : ''}`);
  }
  if (prev.platform !== g.platform) {
    q('.row-platform span').textContent = g.platform;
    q('.row-platform').style.setProperty('--dot', platformColor(g.platform));
  }
  setFlags(el, g, prev);
  if (prev.favorite !== g.favorite || prev.name !== g.name) setFav(el, g);
  el.classList.toggle('is-hidden', !!g.hidden);
  q('.row-last').textContent = g.lastPlayed ? fmtRelative(g.lastPlayed) : 'Nunca jogado';
  q('.row-time').textContent = fmtDuration(g.playSeconds);
  if (prev.art !== g.art || prev.name !== g.name) {
    const a = g.art || {};
    setArt(q('.art'), a.icon
      ? { sources: [], icon: a.icon, name: g.name, seed: g.id }
      : { sources: [a.header, a.capsule], icon: null, name: g.name, seed: g.id });
  }
}
