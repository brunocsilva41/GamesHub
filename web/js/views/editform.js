// Edit form on the details page: name, launch arguments, Steam App ID (with Steam search for artwork).
import { icon } from '../components/icons.js';
import { debounce } from '../util.js';
import { toast } from '../components/toast.js';
import { searchSteamInto } from './steamsearch.js';
import * as A from '../actions.js';

const FIELDS = ['name', 'launchArgs', 'steamAppId'];

export function initEditForm(panel, getId) {
  panel.insertAdjacentHTML('beforeend', `
    <form class="edit-form" autocomplete="off" novalidate>
      <label class="field"><span class="field-label">Nome</span>
        <input class="input" name="name" type="text" maxlength="120" spellcheck="false"></label>
      <label class="field"><span class="field-label">Argumentos de inicialização</span>
        <input class="input mono" name="launchArgs" type="text" placeholder="Ex.: -novid -fullscreen" spellcheck="false"></label>
      <label class="field"><span class="field-label">Steam App ID <small>usado para buscar as artes</small></span>
        <input class="input mono" name="steamAppId" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="Ex.: 730" spellcheck="false"></label>
      <div class="field steam-find">
        <span class="field-label">Encontrar na Steam</span>
        <div class="input-group">
          ${icon('search')}
          <input class="input" type="search" data-steam-q placeholder="Digite o nome do jogo" aria-label="Buscar jogo na Steam" spellcheck="false">
        </div>
        <div class="steam-results" role="listbox" aria-label="Resultados da Steam"></div>
      </div>
      <div class="form-actions">
        <span class="form-hint">Deixe o nome vazio para voltar ao original.</span>
        <button type="button" class="btn btn-ghost" data-edit="reset" disabled>Descartar</button>
        <button type="submit" class="btn btn-primary" data-edit="save" disabled>${icon('check')}Salvar</button>
      </div>
    </form>`);

  const form = panel.querySelector('.edit-form');
  const input = (n) => form.elements[n];
  const results = form.querySelector('.steam-results');
  const steamQ = form.querySelector('[data-steam-q]');
  let original = {};

  const dirty = () => FIELDS.some((f) => input(f).value.trim() !== (original[f] || ''));
  const syncButtons = () => {
    const d = dirty();
    form.querySelector('[data-edit="save"]').disabled = !d;
    form.querySelector('[data-edit="reset"]').disabled = !d;
  };
  form.addEventListener('input', (e) => {
    if (e.target.name === 'steamAppId') e.target.value = e.target.value.replace(/\D/g, '');
    if (e.target !== steamQ) syncButtons();
  });

  const doSearch = debounce(() => searchSteamInto(results, steamQ.value), 350);
  steamQ.addEventListener('input', doSearch);
  steamQ.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); doSearch.flush(); } });
  results.addEventListener('click', (e) => {
    const r = e.target.closest('[data-appid]');
    if (!r) return;
    input('steamAppId').value = r.dataset.appid;
    for (const x of results.querySelectorAll('[data-appid]')) x.setAttribute('aria-selected', String(x === r));
    syncButtons();
    input('steamAppId').focus();
  });

  form.querySelector('[data-edit="reset"]').addEventListener('click', () => {
    for (const f of FIELDS) input(f).value = original[f] || '';
    syncButtons();
  });

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const id = getId();
    if (!id || !dirty()) return;
    const edit = {};
    for (const f of FIELDS) {
      const v = input(f).value.trim();
      if (v !== (original[f] || '')) edit[f] = v;
    }
    const data = await A.updateGame(id, edit, { silent: true });
    if (data) {
      original = { ...original, ...edit };
      syncButtons();
      toast(data.message || 'Alterações salvas.');
    }
  });

  return {
    update(g) {
      const next = { name: g.name || '', launchArgs: g.launchArgs || '', steamAppId: g.steamAppId || '' };
      const wasDirty = dirty();
      for (const f of FIELDS) {
        const el = input(f);
        // Never clobber what the user is typing.
        if (document.activeElement === el || (wasDirty && el.value.trim() !== (original[f] || ''))) continue;
        el.value = next[f];
      }
      if (getId() !== form.dataset.game) {
        form.dataset.game = getId();
        steamQ.value = g.name || '';
        results.innerHTML = `<p class="muted">Pesquise para escolher o jogo certo e baixar as artes corretas.</p>`;
      }
      original = next;
      syncButtons();
    },
  };
}
