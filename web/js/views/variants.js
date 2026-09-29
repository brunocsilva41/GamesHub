// Details → "Variações": launch variants grouped in one card (label, primary, ungroup) and the
// "Agrupar com outro jogo…" picker (searchable multi-select + primary choice → groupVariants).
import { store, searchGames, byName } from '../store.js';
import { icon } from '../components/icons.js';
import { esc, debounce } from '../util.js';
import { openModal } from '../components/modal.js';
import * as A from '../actions.js';
import * as W from '../actions2.js';

const tail = (id) => String(id).replace(/^[a-z]+:/i, '');

export function initVariants(panel, getId) {
  panel.innerHTML = `
    <div class="panel-head"><h2 class="panel-title" id="d-variants">${icon('layers')}Variações</h2>
      <button type="button" class="btn btn-ghost btn-sm" data-nav data-vr="pick">${icon('plus')}Agrupar com outro jogo…</button></div>
    <p class="panel-hint">Versões do mesmo jogo (ex.: DirectX 11, mods, edições) ficam em um só card. Escolha qual abrir em “Jogar como”.</p>
    <div class="vr-body" aria-live="polite"></div>`;
  const body = panel.querySelector('.vr-body');
  let group = null;
  let key = '';
  let seq = 0;

  panel.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-vr]');
    const id = getId();
    if (!b || !id) return;
    const act = b.dataset.vr;
    if (act === 'pick') openGroupPicker(id);
    else if (act === 'play') A.launch(id, b.dataset.id);
    else if (act === 'primary' && group) W.setVariantPrimary(group.id, b.dataset.id, group.memberIds);
    else if (act === 'ungroup' && group) W.ungroupVariants(group.id);
  });
  panel.addEventListener('change', (e) => {
    const input = e.target.closest('[data-vr-label]');
    if (!input) return;
    const label = input.value.trim();
    if (label !== (input.dataset.saved || '')) { input.dataset.saved = label; W.setVariantLabel(input.dataset.vrLabel, label); }
  });
  panel.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.matches('[data-vr-label]')) { e.preventDefault(); e.target.blur(); }
  });

  function render(g, variants) {
    const primary = group?.primaryId || g.id;
    const rows = variants.map((v, i) => {
      const isPrimary = v.id === primary;
      const own = store.get(v.id);
      return `<li class="vr-item">` +
        `<span class="vr-name"><input class="input input-sm" type="text" maxlength="60" spellcheck="false" data-nav data-vr-label="${esc(v.id)}" data-saved="${esc(v.label)}" value="${esc(v.label)}" aria-label="Nome da variação ${i + 1}">` +
        `<small class="mono" title="${esc(v.id)}">${esc(own?.name || tail(v.id))}</small></span>` +
        (isPrimary ? '<span class="chip chip-soft vr-primary">Principal</span>'
          : `<button type="button" class="btn btn-ghost btn-sm" data-nav data-vr="primary" data-id="${esc(v.id)}"${group ? '' : ' disabled'}>Tornar principal</button>`) +
        `<button type="button" class="icon-btn" data-nav data-vr="play" data-id="${esc(v.id)}" aria-label="Jogar ${esc(v.label || tail(v.id))}" title="Jogar esta variação">${icon('play')}</button></li>`;
    }).join('');
    body.innerHTML = `<ul class="vr-list">${rows}</ul>` +
      `<div class="vr-foot"><button type="button" class="btn btn-danger-ghost btn-sm" data-nav data-vr="ungroup"${group ? '' : ' disabled'}>${icon('layers')}Desagrupar</button></div>`;
  }

  return {
    async update(g) {
      const variants = A.variantsOf(g);
      const next = JSON.stringify([g.id, variants]);
      if (next === key || panel.contains(document.activeElement) && document.activeElement.matches('[data-vr-label]')) return;
      key = next;
      if (!variants.length) {
        group = null;
        body.innerHTML = '<p class="muted">Este jogo não tem variações.</p>';
        return;
      }
      const my = ++seq;
      render(g, variants); // immediately (buttons needing the group id stay disabled)
      const found = await W.variantGroupOf(g.id);
      if (my !== seq) return;
      group = found;
      render(store.get(g.id) || g, variants);
    },
  };
}

/** Modal: pick games to group with `id` (current members stay), choose the primary, confirm. */
export function openGroupPicker(id) {
  const g = store.get(id);
  if (!g) return;
  const members = [g.id, ...A.variantsOf(g).map((v) => v.id).filter((x) => x !== g.id)];
  const pool = store.state.games.filter((x) => !x.hidden && !members.includes(x.id)).sort(byName);
  const selected = new Set();
  let primary = g.id;

  const body = document.createElement('form');
  body.className = 'modal-body pick';
  body.innerHTML = `
    <p class="modal-text">Escolha as outras versões de <b>${esc(g.name)}</b>. Elas passam a abrir pelo mesmo card.</p>
    <div class="input-group">${icon('search')}<input class="input" type="search" data-pick-q placeholder="Buscar jogos" aria-label="Buscar jogos da biblioteca" spellcheck="false"></div>
    <div class="pick-list" role="group" aria-label="Jogos da biblioteca" data-arrow-nav></div>
    <fieldset class="pick-primary"><legend class="field-label">Versão principal (mostrada no card)</legend><div class="pick-radios" data-arrow-nav></div></fieldset>
    <div class="modal-actions"><button type="button" class="btn btn-ghost" data-nav data-pick="cancel">Cancelar</button>
      <button type="submit" class="btn btn-primary" data-nav disabled>${icon('layers')}Agrupar</button></div>`;
  const list = body.querySelector('.pick-list');
  const radios = body.querySelector('.pick-radios');
  const q = body.querySelector('[data-pick-q]');
  const nameOf = (x) => store.get(x)?.name || tail(x);

  function renderList() {
    const found = searchGames(pool, q.value);
    list.innerHTML = found.length ? found.slice(0, 200).map((x) =>
      `<label class="pick-row"><input type="checkbox" data-nav value="${esc(x.id)}"${selected.has(x.id) ? ' checked' : ''}>` +
      `<span class="pick-name">${esc(x.name)}</span><small>${esc(x.platform)}</small></label>`).join('')
      : '<p class="muted">Nenhum jogo encontrado.</p>';
  }
  function renderRadios() {
    const ids = [...members, ...selected];
    if (!ids.includes(primary)) primary = g.id;
    radios.innerHTML = ids.map((x) => `<label class="pick-row"><input type="radio" name="primary" data-nav value="${esc(x)}"${x === primary ? ' checked' : ''}>` +
      `<span class="pick-name">${esc(nameOf(x))}</span></label>`).join('');
    body.querySelector('[type="submit"]').disabled = selected.size === 0;
  }

  q.addEventListener('input', debounce(renderList, 120));
  body.addEventListener('change', (e) => {
    const t = e.target;
    if (t.type === 'checkbox') { if (t.checked) selected.add(t.value); else selected.delete(t.value); renderRadios(); }
    else if (t.type === 'radio') primary = t.value;
  });
  const m = openModal({ title: 'Agrupar variações', body, className: 'modal-pick', initialFocus: '[data-pick-q]' });
  body.querySelector('[data-pick="cancel"]').addEventListener('click', () => m.close());
  body.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!selected.size) return;
    const btn = body.querySelector('[type="submit"]');
    btn.disabled = true;
    const ok = await W.groupVariants([...members, ...selected], primary);
    if (ok) m.close(true); else btn.disabled = false;
  });
  renderList();
  renderRadios();
}
