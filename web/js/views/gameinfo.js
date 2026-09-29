// Details → store info inside "Informações" (lazy getGameInfo) and the side actions that depend on it:
// Correções e dicas (PCGamingWiki), Verificar arquivos, Desinstalar….
import { store } from '../store.js';
import { icon } from '../components/icons.js';
import { esc, fmtBytes, fmtDuration, fmtDate } from '../util.js';
import * as A from '../actions.js';
import * as W from '../actions2.js';

export const SIDE_ACTIONS_HTML =
  `<button type="button" class="btn btn-ghost btn-block" data-nav data-gi="pcgw">${icon('wrench')}Correções e dicas (PCGamingWiki)</button>` +
  `<button type="button" class="btn btn-ghost btn-block" data-nav data-gi="validate" hidden>${icon('shield')}Verificar arquivos</button>` +
  `<button type="button" class="btn btn-ghost btn-block" data-nav data-gi="uninstall" aria-disabled="true">${icon('trash')}Desinstalar…</button>`;

const SKELETON = '<div class="gi-skeleton" aria-hidden="true"><span class="sk-line"></span><span class="sk-line"></span><span class="sk-line short"></span></div>' +
  '<span class="sr-only">Carregando informações da loja…</span>';
const METHOD_LABEL = { steam: 'pela Steam', epic: 'pela Epic Games Launcher', registry: 'pelo desinstalador do Windows' };
const NO_UNINSTALLER = 'Nenhum desinstalador encontrado. Use Configurações do Windows › Aplicativos.';

export const pcgwSearchUrl = (g) => `https://www.pcgamingwiki.com/w/index.php?search=${encodeURIComponent(g?.name || '')}`;

function hostOf(url) {
  try { return new URL(url).host.replace(/^www\./, ''); } catch { return url; }
}

const mcTier = (n) => (n >= 75 ? 'good' : n >= 50 ? 'mid' : 'bad');

/** Markup for the store-info block (pure; exported for tests). */
export function renderInfoHtml(g, d) {
  const info = d?.info || null;
  const parts = [];
  const genres = info?.genres?.length ? info.genres : g?.genres || [];
  if (info?.shortDescription) parts.push(`<p class="gi-desc">${esc(info.shortDescription)}</p>`);
  const chips = [
    ...genres.map((x) => `<span class="chip">${esc(x)}</span>`),
    ...(info?.categories || []).map((x) => `<span class="chip chip-soft">${esc(x)}</span>`),
  ];
  if (info?.controllerSupport && !(info.categories || []).some((c) => /controle/i.test(c))) {
    chips.push(`<span class="chip chip-soft">${icon('gamepad')}Suporte a controle</span>`);
  }
  if (chips.length) parts.push(`<div class="gi-chips" role="list" aria-label="Gêneros e categorias">${chips.join('').replaceAll('<span class="chip', '<span role="listitem" class="chip')}</div>`);
  if (!info) parts.push('<p class="muted gi-none">Sem informações da loja para este jogo.</p>');

  const rows = [];
  if (info?.developers?.length) rows.push(['Desenvolvedor', esc(info.developers.join(', '))]);
  if (info?.publishers?.length) rows.push(['Distribuidora', esc(info.publishers.join(', '))]);
  if (info?.releaseDate) rows.push(['Lançamento', esc(info.releaseDate)]);
  if (info?.metacritic > 0) {
    const n = Math.round(info.metacritic);
    rows.push(['Metacritic', `<span class="mc mc-${mcTier(n)}" title="Nota no Metacritic">${n}</span>`]);
  }
  const size = Number(d?.sizeBytes) > 0 ? d.sizeBytes : Number(g?.sizeBytes) > 0 ? g.sizeBytes : -1;
  rows.push(['Tamanho em disco', size > 0 ? esc(fmtBytes(size)) : 'Desconhecido']);
  if (info?.website) {
    rows.push(['Site oficial', `<a href="#" class="gi-link" data-nav data-gi-link="${esc(info.website)}">${esc(hostOf(info.website))}${icon('external')}</a>`]);
  }
  parts.push(`<dl class="stats gi-meta">${rows.map(([k, v]) => `<div class="stat"><dt>${k}</dt><dd>${v}</dd></div>`).join('')}</dl>`);

  const st = d?.steam;
  if (st) {
    const mins = Number(st.playtimeMinutes) || 0;
    let text = mins > 0 ? `A Steam registrou ${esc(fmtDuration(mins * 60))} de jogo` : 'A Steam ainda não registrou tempo de jogo';
    if (st.lastPlayed) text += ` · última vez em ${esc(fmtDate(st.lastPlayed))}`;
    text += '.';
    if (st.updatePending) text += ' <b class="gi-warn">Há uma atualização pendente na Steam.</b>';
    parts.push(`<p class="gi-note">${icon('steam')}<span>${text}</span></p>`);
  }
  return parts.join('');
}

export function initGameInfo(body, getId) {
  const box = body.querySelector('.store-info');
  const side = body.querySelector('.side-actions');
  let data = null;
  let seq = 0;

  side.addEventListener('click', (e) => {
    const b = e.target.closest('[data-gi]');
    const id = getId();
    if (!b || !id || b.getAttribute('aria-disabled') === 'true') return;
    switch (b.dataset.gi) {
      case 'pcgw': W.openExternal(data?.pcgwUrl || pcgwSearchUrl(store.get(id))); break;
      case 'validate': W.validate(id); break;
      case 'uninstall': W.uninstall(id, data?.uninstall); break;
    }
  });
  box.addEventListener('click', (e) => {
    const link = e.target.closest('[data-gi-link]');
    if (link) { e.preventDefault(); W.openExternal(link.dataset.giLink); return; }
    if (e.target.closest('[data-gi-retry]') && getId()) load(getId());
  });

  function syncSide(d, failed = false) {
    side.querySelector('[data-gi="validate"]').hidden = !d?.canValidate;
    const un = side.querySelector('[data-gi="uninstall"]');
    const method = d?.uninstall?.method;
    const usable = !!d && !!METHOD_LABEL[method];
    un.setAttribute('aria-disabled', String(!usable));
    un.title = !d ? (failed ? 'Não foi possível verificar o desinstalador.' : 'Procurando o desinstalador…')
      : usable ? `Desinstalar ${METHOD_LABEL[method]}` : NO_UNINSTALLER;
  }

  async function load(id) {
    const my = ++seq;
    data = null;
    box.setAttribute('aria-busy', 'true');
    box.innerHTML = SKELETON;
    syncSide(null);
    let d = null, err = null;
    try { d = await A.getBridge().call('getGameInfo', { id }); } catch (e) { err = e; }
    if (my !== seq || getId() !== id) return;
    box.removeAttribute('aria-busy');
    if (err) {
      box.innerHTML = `<p class="muted err">Não foi possível carregar as informações. ${esc(err.message)}</p>` +
        `<button type="button" class="btn btn-ghost btn-sm" data-nav data-gi-retry>${icon('refresh')}Tentar novamente</button>`;
      syncSide(null, true);
      return;
    }
    data = d;
    box._html = renderInfoHtml(store.get(id), d);
    box.innerHTML = box._html;
    syncSide(d);
  }

  return {
    load,
    /** games event: only the size/genres fallback can change; re-render cheaply when loaded. */
    update(g) {
      if (!data || box.contains(document.activeElement)) return;
      const html = renderInfoHtml(g, data);
      if (box._html !== html) { box._html = html; box.innerHTML = html; }
    },
  };
}
