// "Encontrar jogos no PC" (Add game modal): runs discoverGames, lists the candidates grouped by confidence with a
// checkbox (preselected when confidence >= 0.8) and an editable name, then adds the selection via addDiscovered.
// Pure helpers live in discover-model.js.
import { plural } from '../util.js';
import { icon } from '../components/icons.js';
import { toast } from '../components/toast.js';
import * as A from '../actions.js';
import { buildPayload, initialSelection, removeAdded, resultsHtml } from './discover-model.js';

const INTRO = '<p class="muted">Procura jogos instalados fora das lojas já importadas: pastas de jogos em todos os discos, ' +
  'Xbox / Microsoft Store, GOG, EA, Ubisoft, Battle.net e programas instalados. Nada é alterado no seu PC.</p>';

/** Renders the discovery pane inside `root`. Returns { focus, isBusy } for the tab switcher. */
export function mountDiscover(root) {
  let list = [];
  let selected = new Set();
  let names = new Map();
  let busy = false;
  root.classList.add('disc');

  const startBtn = (label, cls = 'btn-primary') => `<button type="button" class="btn ${cls}" data-nav data-disc="start">${icon('search')}${label}</button>`;
  const set = (html) => { root.innerHTML = html; };
  const idle = () => set(`${INTRO}<div class="disc-actions">${startBtn('Procurar jogos no PC')}</div>`);

  function renderResults() {
    if (!list.length) {
      set(`<div class="disc-empty">${icon('search')}<strong>Nenhum jogo novo encontrado</strong>` +
        '<span class="muted">Os jogos instalados já estão na biblioteca ou ficam fora dos locais usuais. ' +
        'Use a aba Arquivo para adicioná-los.</span></div>' +
        `<div class="disc-actions">${startBtn('Procurar de novo')}</div><ul class="add-results" aria-live="polite"></ul>`);
      return;
    }
    set(`<p class="muted" aria-live="polite">${plural(list.length, 'jogo encontrado', 'jogos encontrados')}. ` +
      'Revise os nomes e escolha o que adicionar.</p>' +
      `<div class="disc-scroll">${resultsHtml(list, selected)}</div>` +
      '<div class="disc-actions">' +
      `<button type="button" class="btn btn-ghost btn-sm" data-nav data-disc="all">${icon('check')}Marcar todos</button>` +
      '<button type="button" class="btn btn-ghost btn-sm" data-nav data-disc="none">Desmarcar todos</button>' +
      startBtn('Procurar de novo', 'btn-ghost btn-sm') +
      `<button type="button" class="btn btn-primary" data-nav data-disc="add">${icon('plus')}<span>Adicionar selecionados</span></button>` +
      '</div><ul class="add-results" aria-live="polite"></ul>');
    syncAddButton();
  }

  function syncAddButton() {
    const add = root.querySelector('[data-disc="add"]');
    if (!add) return;
    add.disabled = busy || selected.size === 0;
    add.querySelector('span').textContent = selected.size ? `Adicionar selecionados (${selected.size})` : 'Adicionar selecionados';
  }

  function showOutcome(results) {
    const ul = root.querySelector('.add-results');
    if (!ul) return;
    for (const r of results) {
      const li = document.createElement('li');
      li.className = r.ok ? 'ok' : 'err';
      li.innerHTML = icon(r.ok ? 'check' : 'alert');
      li.append(document.createTextNode(r.message || (r.ok ? 'Adicionado.' : 'Falhou.')));
      ul.prepend(li);
    }
  }

  async function start() {
    if (busy) return;
    busy = true;
    set('<div class="disc-running" role="status"><span class="spinner" aria-hidden="true"></span>' +
      '<div><strong>Procurando jogos…</strong><span class="muted" data-disc-progress>Isso leva alguns segundos.</span></div></div>');
    const progress = root.querySelector('[data-disc-progress]');
    const off = A.getBridge()?.on('discoverProgress', (d) => { if (d?.text) progress.textContent = d.text; });
    const data = await A.run('discoverGames', {});
    off?.();
    busy = false;
    if (!data) { idle(); focus(); return; }
    list = Array.isArray(data.candidates) ? data.candidates : [];
    selected = initialSelection(list);
    names = new Map();
    renderResults();
    focus();
  }

  async function add() {
    const items = buildPayload(list, selected, names);
    if (!items.length || busy) return;
    busy = true;
    syncAddButton();
    const data = await A.run('addDiscovered', { items });
    busy = false;
    const results = data?.results || [];
    const next = removeAdded(list, selected, names, results);
    if (next.added) {
      toast(next.added === 1 ? '1 jogo adicionado à biblioteca.' : `${next.added} jogos adicionados à biblioteca.`, { kind: 'ok' });
      ({ list, selected, names } = next);
      renderResults();
    } else syncAddButton();
    showOutcome(results);
    focus();
  }

  function focus() {
    (root.querySelector('[data-disc-pick]') || root.querySelector('[data-disc="start"]'))?.focus();
  }

  root.addEventListener('click', (e) => {
    const b = e.target.closest('[data-disc]');
    if (!b || b.disabled) return;
    const act = b.dataset.disc;
    if (act === 'start') start();
    else if (act === 'add') add();
    else if (act === 'all' || act === 'none') {
      selected = act === 'all' ? new Set(list.map((_, i) => i)) : new Set();
      for (const cb of root.querySelectorAll('[data-disc-pick]')) cb.checked = act === 'all';
      syncAddButton();
    }
  });
  root.addEventListener('change', (e) => {
    const i = e.target.dataset?.discPick;
    if (i === undefined) return;
    if (e.target.checked) selected.add(Number(i)); else selected.delete(Number(i));
    syncAddButton();
  });
  root.addEventListener('input', (e) => {
    const i = e.target.dataset?.discName;
    if (i !== undefined) names.set(Number(i), e.target.value);
  });
  root.addEventListener('keydown', (e) => {
    // Enter in a name field confirms the edit and moves on to "Adicionar selecionados".
    if (e.key !== 'Enter' || e.target.dataset?.discName === undefined) return;
    e.preventDefault();
    root.querySelector('[data-disc="add"]')?.focus();
  });

  idle();
  return { focus, isBusy: () => busy };
}
