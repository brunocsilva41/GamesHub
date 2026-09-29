// Details → "Saves e configurações": save/config folders from PCGamingWiki, resolved on this PC (getPcgw).
import { store } from '../store.js';
import { icon } from '../components/icons.js';
import { esc } from '../util.js';
import { toast } from '../components/toast.js';
import * as A from '../actions.js';
import * as W from '../actions2.js';
import { pcgwSearchUrl } from './gameinfo.js';

function itemHtml(p) {
  const shown = p.path || p.raw || '—';
  const tip = p.raw ? `No PCGamingWiki: ${p.raw}` : '';
  const action = p.exists && p.path
    ? `<button type="button" class="btn btn-ghost btn-sm" data-nav data-sv="open" data-path="${esc(p.path)}" aria-label="Abrir ${esc(p.path)}">${icon('folder')}Abrir</button>`
    : '<span class="sv-missing">não encontrado</span>';
  return `<li class="sv-item${p.exists ? '' : ' is-missing'}"><code class="sv-path" title="${esc(tip)}">${esc(shown)}</code>${action}</li>`;
}

function listHtml(title, list) {
  const items = (list || []).length ? list.map(itemHtml).join('') : '<li class="muted">Nenhum local informado.</li>';
  return `<div class="sv-group"><h3 class="sv-title">${title}</h3><ul class="sv-list">${items}</ul></div>`;
}

/** Markup for a getPcgw reply (pure; exported for tests). */
export function renderPcgwHtml(info, g) {
  if (!info || !info.found) {
    return `<p class="muted">Não encontrado no PCGamingWiki.</p>` +
      `<a href="#" class="gi-link" data-nav data-sv-link="${esc(pcgwSearchUrl(g))}">Pesquisar no PCGamingWiki${icon('external')}</a>`;
  }
  return listHtml('Saves', info.saveLocations) + listHtml('Configurações', info.configLocations) +
    (info.pageUrl ? `<a href="#" class="gi-link" data-nav data-sv-link="${esc(info.pageUrl)}">Ver ${esc(info.title || 'a página')} no PCGamingWiki${icon('external')}</a>` : '');
}

export function initSaves(panel, getId) {
  panel.innerHTML = `
    <div class="panel-head"><h2 class="panel-title" id="d-saves">${icon('save')}Saves e configurações</h2>
      <button type="button" class="btn btn-ghost btn-sm" data-nav data-sv="find">${icon('search')}Localizar</button></div>
    <p class="panel-hint">Descubra onde este jogo guarda seus saves e configurações — útil para backup. Dados do PCGamingWiki.</p>
    <div class="sv-body" aria-live="polite"></div>`;
  const body = panel.querySelector('.sv-body');
  const findBtn = panel.querySelector('[data-sv="find"]');
  let seq = 0;

  panel.addEventListener('click', async (e) => {
    const link = e.target.closest('[data-sv-link]');
    if (link) { e.preventDefault(); W.openExternal(link.dataset.svLink); return; }
    const b = e.target.closest('[data-sv]');
    if (!b) return;
    if (b.dataset.sv === 'find') find();
    else if (b.dataset.sv === 'open') {
      const d = await A.run('openPath', { path: b.dataset.path });
      if (d?.message) toast(d.message, { kind: 'info' });
    }
  });

  async function find() {
    const id = getId();
    if (!id) return;
    const my = ++seq;
    findBtn.disabled = true;
    body.setAttribute('aria-busy', 'true');
    body.innerHTML = '<p class="muted"><span class="spinner" aria-hidden="true"></span>Consultando o PCGamingWiki…</p>';
    let info = null, err = null;
    try { info = await A.getBridge().call('getPcgw', { id }); } catch (e2) { err = e2; }
    if (my !== seq || getId() !== id) return;
    findBtn.disabled = false;
    body.removeAttribute('aria-busy');
    findBtn.innerHTML = `${icon('refresh')}Procurar de novo`;
    body.innerHTML = err
      ? `<p class="muted err">Não foi possível consultar o PCGamingWiki. ${esc(err.message)}</p>`
      : renderPcgwHtml(info, store.get(id));
  }

  return {
    /** New game on the details page: back to the initial (not searched) state. */
    reset() {
      seq++;
      findBtn.disabled = false;
      findBtn.innerHTML = `${icon('search')}Localizar`;
      body.removeAttribute('aria-busy');
      body.innerHTML = store.state.settings.pcgwEnabled === false
        ? '<p class="muted">A consulta ao PCGamingWiki está desativada nas Configurações.</p>' : '';
    },
  };
}
