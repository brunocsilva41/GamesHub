// Automation editor ("Antes e depois de jogar"): per-game profile (id) or the default profile (id = null).
import { store } from '../store.js';
import { icon } from '../components/icons.js';
import { esc } from '../util.js';
import { toast } from '../components/toast.js';
import * as A from '../actions.js';
import * as M from './automation-model.js';

let optionsPromise = null;
/** automationOptions is cached for the session (enumerating devices is slow); failures are retried. */
export function loadOptions() {
  if (!optionsPromise) {
    optionsPromise = A.getBridge().call('automationOptions').catch((err) => { optionsPromise = null; throw err; });
  }
  return optionsPromise;
}

const LISTS = [['before', 'Antes de jogar', 'Executadas antes de abrir o jogo.'], ['after', 'Depois de jogar', 'Executadas quando o jogo fecha.']];

export function createAutomationEditor(root, { perGame }) {
  root.classList.add('auto-editor');
  root.innerHTML = `
    <p class="auto-off panel-warn" hidden>${icon('alert')}<span>A automação está desativada. Ative “Automação antes e depois de jogar” em Configurações.</span></p>
    <label class="auto-toggle"><input type="checkbox" class="switch" role="switch" data-nav data-au="enabled">
      <span>${perGame ? 'Usar automação neste jogo' : 'Ativar a automação padrão'}</span></label>
    ${perGame ? '<label class="auto-toggle"><input type="checkbox" class="switch" role="switch" data-nav data-au="useDefault"><span>Também executar a automação padrão</span></label>' : ''}
    <div class="auto-lists">${LISTS.map(([k, t, d]) => `
      <section class="auto-list" data-list="${k}" aria-label="${t}"><h3 class="sv-title">${t} <small>${d}</small></h3>
        <ol class="auto-actions"></ol>
        <button type="button" class="btn btn-ghost btn-sm" data-nav data-au-add="${k}">${icon('plus')}Adicionar ação</button></section>`).join('')}</div>
    <p class="panel-hint auto-note">${icon('info')}<span>Plano de energia, saída de áudio e resolução alterados em “Antes de jogar” voltam ao normal sozinhos quando o jogo fecha — para isso, o GamesHub precisa detectar o jogo em execução.</span></p>
    <div class="form-actions"><span class="form-hint auto-status" role="status" aria-live="polite"></span>
      <button type="button" class="btn btn-ghost" data-nav data-au="reset" disabled>Descartar</button>
      <button type="button" class="btn btn-primary" data-nav data-au="save" disabled>${icon('check')}Salvar</button></div>`;

  const status = root.querySelector('.auto-status');
  let id = null, model = null, saved = null, options = null, optionsError = '', errors = null, seq = 0;

  const dirty = () => !!model && !!saved && !M.sameProfile(model, saved);
  function sync() {
    const d = dirty();
    root.querySelector('[data-au="save"]').disabled = !d;
    root.querySelector('[data-au="reset"]').disabled = !d;
    root.querySelector('.auto-off').hidden = store.state.settings.automationEnabled !== false;
    if (d && !errors) status.textContent = 'Alterações não salvas.';
    else if (!d && !errors) status.textContent = '';
  }

  function paramsHtml(a, n) {
    const t = `data-nav data-f="target" aria-label="${n}`;
    switch (a.type) {
      case 'run': return `<input class="input input-sm mono" ${t}: programa" value="${esc(a.target)}" placeholder="C:\\Programas\\app.exe, atalho ou URL" spellcheck="false">` +
        `<input class="input input-sm mono" data-nav data-f="args" aria-label="${n}: argumentos" value="${esc(a.args)}" placeholder="Argumentos (opcional)" spellcheck="false">`;
      case 'close': return `<input class="input input-sm mono" ${t}: processo" value="${esc(a.target)}" placeholder="Ex.: Discord.exe" spellcheck="false">`;
      case 'wait': return `<input class="input input-sm auto-sec" type="number" min="1" max="${M.MAX_WAIT}" step="1" data-nav data-f="seconds" aria-label="${n}: segundos" value="${a.seconds}"><span class="muted">segundos</span>`;
      default: {
        const list = options?.[M.OPTION_LISTS[a.type]];
        if (!list) return `<span class="muted${optionsError ? ' err' : ''}">${optionsError ? 'Não foi possível listar as opções.' : '<span class="spinner" aria-hidden="true"></span>Carregando opções…'}</span>`;
        const known = list.some((o) => o.id === a.target);
        const opts = ['<option value="">Escolha…</option>',
          ...(!known && a.target ? [`<option value="${esc(a.target)}">${esc(a.target)} (indisponível)</option>`] : []),
          ...list.map((o) => `<option value="${esc(o.id)}">${esc(o.name)}${o.current ? ' (atual)' : ''}</option>`)];
        return `<span class="select-wrap"><select class="select select-sm" ${t}: opção">${opts.join('')}</select>${icon('chevronDown')}</span>`;
      }
    }
  }

  function rowHtml(listKey, a, i, total) {
    const n = `${listKey === 'before' ? 'Antes' : 'Depois'}, ação ${i + 1}`;
    const err = errors?.[listKey]?.[i] || '';
    const errId = `au-${listKey}-${i}-${seq}`;
    return `<li class="auto-row${a.enabled ? '' : ' is-off'}${err ? ' is-invalid' : ''}" data-i="${i}">` +
      `<span class="select-wrap"><select class="select select-sm" data-nav data-f="type" aria-label="${n}: tipo"${err ? ` aria-describedby="${errId}"` : ''}>` +
      M.ACTION_TYPES.map((t) => `<option value="${t.key}"${t.key === a.type ? ' selected' : ''}>${t.label}</option>`).join('') + `</select>${icon('chevronDown')}</span>` +
      `<span class="auto-params">${paramsHtml(a, n)}</span>` +
      `<span class="auto-tools"><input type="checkbox" class="switch" role="switch" data-nav data-f="enabled" aria-label="${n}: ativa" title="Ativa"${a.enabled ? ' checked' : ''}>` +
      `<button type="button" class="icon-btn" data-nav data-au-move="-1" aria-label="${n}: mover para cima" title="Mover para cima"${i === 0 ? ' disabled' : ''}>${icon('arrowUp')}</button>` +
      `<button type="button" class="icon-btn" data-nav data-au-move="1" aria-label="${n}: mover para baixo" title="Mover para baixo"${i === total - 1 ? ' disabled' : ''}>${icon('arrowDown')}</button>` +
      `<button type="button" class="icon-btn danger" data-nav data-au-del aria-label="${n}: remover" title="Remover">${icon('trash')}</button></span>` +
      (err ? `<span class="auto-err" id="${errId}">${esc(err)}</span>` : '') + '</li>';
  }

  function renderLists(focus = null) {
    for (const [k] of LISTS) {
      const ol = root.querySelector(`[data-list="${k}"] .auto-actions`);
      const list = model[k];
      ol.innerHTML = list.length ? list.map((a, i) => rowHtml(k, a, i, list.length)).join('')
        : '<li class="muted auto-empty">Nenhuma ação.</li>';
      for (const sel of ol.querySelectorAll('select[data-f="target"]')) sel.value = list[+sel.closest('[data-i]').dataset.i].target;
      root.querySelector(`[data-au-add="${k}"]`).disabled = list.length >= M.MAX_ACTIONS;
    }
    if (focus) root.querySelector(`[data-list="${focus.list}"] [data-i="${focus.i}"] ${focus.sel}`)?.focus({ preventScroll: false });
    sync();
  }

  function render() {
    root.querySelector('[data-au="enabled"]').checked = model.enabled;
    const ud = root.querySelector('[data-au="useDefault"]');
    if (ud) ud.checked = model.useDefault;
    root.querySelector('.auto-lists').classList.toggle('is-off', !model.enabled);
    renderLists();
  }

  const at = (el) => {
    const row = el.closest('[data-i]');
    const k = el.closest('[data-list]')?.dataset.list;
    return row && k ? { k, i: +row.dataset.i, a: model[k][+row.dataset.i] } : null;
  };

  root.addEventListener('input', (e) => {
    const f = e.target.dataset.f;
    const p = model && f && at(e.target);
    if (!p || f === 'type' || f === 'enabled') return;
    if (f === 'seconds') p.a.seconds = Math.round(Number(e.target.value) || 0); else p.a[f] = e.target.value;
    sync();
  });
  root.addEventListener('change', (e) => {
    if (!model) return;
    const t = e.target;
    if (t.dataset.au === 'enabled' || t.dataset.au === 'useDefault') {
      model[t.dataset.au] = t.checked;
      root.querySelector('.auto-lists').classList.toggle('is-off', !model.enabled);
      sync();
      return;
    }
    const p = at(t);
    if (!p) return;
    if (t.dataset.f === 'type') { model[p.k][p.i] = M.blankAction(t.value); model[p.k][p.i].enabled = p.a.enabled; renderLists({ list: p.k, i: p.i, sel: '[data-f="type"]' }); }
    else if (t.dataset.f === 'enabled') { p.a.enabled = t.checked; t.closest('.auto-row').classList.toggle('is-off', !t.checked); sync(); }
    else if (t.dataset.f === 'target' && t.tagName === 'SELECT') { p.a.target = t.value; sync(); }
  });
  root.addEventListener('click', async (e) => {
    const b = e.target.closest('button');
    if (!b || !model || b.disabled) return;
    const p = at(b);
    if (b.dataset.auAdd) {
      const k = b.dataset.auAdd;
      model[k].push(M.blankAction('run'));
      renderLists({ list: k, i: model[k].length - 1, sel: '[data-f="type"]' });
    } else if (b.dataset.auMove && p) {
      const j = M.moveAction(model[p.k], p.i, +b.dataset.auMove);
      if (errors) errors = null;
      const sel = `[data-au-move="${b.dataset.auMove}"]`;
      renderLists({ list: p.k, i: j, sel: (j === 0 || j === model[p.k].length - 1) ? '[data-f="type"]' : sel });
    } else if ('auDel' in b.dataset && p) {
      model[p.k].splice(p.i, 1);
      errors = null;
      renderLists(model[p.k].length ? { list: p.k, i: Math.min(p.i, model[p.k].length - 1), sel: '[data-f="type"]' } : null);
    } else if (b.dataset.au === 'reset') {
      model = M.normalizeProfile(saved); errors = null; status.textContent = ''; render();
    } else if (b.dataset.au === 'save') save();
  });

  async function save() {
    const v = M.validateProfile(model, options);
    if (v.count) {
      errors = v;
      renderLists();
      status.textContent = v.count === 1 ? 'Corrija 1 ação antes de salvar.' : `Corrija ${v.count} ações antes de salvar.`;
      root.querySelector('.auto-row.is-invalid [data-f="target"], .auto-row.is-invalid [data-f="seconds"], .auto-row.is-invalid [data-f="type"]')?.focus();
      return;
    }
    errors = null;
    const btn = root.querySelector('[data-au="save"]');
    btn.disabled = true;
    const data = await A.run('saveAutomation', { id, profile: M.profilePayload(model) });
    if (data) {
      saved = M.normalizeProfile(M.profilePayload(model));
      toast(data.message || 'Automação salva.');
    }
    renderLists();
  }

  return {
    isDirty: dirty,
    /** Loads the profile for `gameId` (null = default profile) and the option lists. */
    async load(gameId) {
      const my = ++seq;
      id = gameId ?? null;
      model = null; saved = null; errors = null;
      for (const [k] of LISTS) root.querySelector(`[data-list="${k}"] .auto-actions`).innerHTML = '<li class="muted auto-empty"><span class="spinner" aria-hidden="true"></span>Carregando…</li>';
      loadOptions().then((o) => { options = o; optionsError = ''; }, (err) => { optionsError = err.message; })
        .then(() => { if (my === seq && model) renderLists(); });
      try {
        const p = await A.getBridge().call('getAutomation', { id });
        if (my !== seq) return;
        model = M.normalizeProfile(p);
        saved = M.normalizeProfile(p);
        render();
      } catch (err) {
        if (my !== seq) return;
        for (const [k] of LISTS) root.querySelector(`[data-list="${k}"] .auto-actions`).innerHTML = `<li class="muted err">${esc(err.message)}</li>`;
      }
    },
    refresh: sync,
  };
}
