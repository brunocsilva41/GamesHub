// "Adicionar jogo" modal: file drop zone / native picker, and a Steam tab (search -> addSteam).
import { openModal } from '../components/modal.js';
import { makeDropTarget } from '../components/dropzone.js';
import { icon } from '../components/icons.js';
import { debounce } from '../util.js';
import { searchSteamInto } from './steamsearch.js';
import * as A from '../actions.js';

let open = null;

export function openAddGame(tab = 'file') {
  if (open) { open.selectTab(tab); return; }
  const body = document.createElement('div');
  body.className = 'modal-body add-game';
  body.innerHTML = `
    <div class="tabs" role="tablist" aria-label="Como adicionar">
      <button type="button" class="tab" role="tab" id="tab-file" aria-controls="pane-file" data-tab="file">${icon('file')}Arquivo</button>
      <button type="button" class="tab" role="tab" id="tab-steam" aria-controls="pane-steam" data-tab="steam">${icon('steam')}Steam</button>
    </div>
    <div class="tab-pane" role="tabpanel" id="pane-file" aria-labelledby="tab-file">
      <div class="dropzone" tabindex="-1">
        <span class="dz-ic">${icon('upload')}</span>
        <strong>Arraste atalhos ou executáveis para cá</strong>
        <span class="muted">Arquivos <b>.exe</b>, <b>.lnk</b> ou <b>.url</b> — vários de uma vez.</span>
        <button type="button" class="btn btn-primary" data-add="pick">${icon('folder')}Procurar arquivo…</button>
      </div>
      <ul class="add-results" aria-live="polite"></ul>
    </div>
    <div class="tab-pane" role="tabpanel" id="pane-steam" aria-labelledby="tab-steam" hidden>
      <div class="input-group">
        ${icon('search')}
        <input class="input" type="search" data-steam-q placeholder="Nome do jogo ou App ID" aria-label="Buscar na Steam" spellcheck="false">
      </div>
      <div class="steam-results steam-results-tall" role="listbox" aria-label="Resultados"><p class="muted">Busque por nome (ex.: “Hollow Knight”) ou App ID (ex.: 367520).</p></div>
    </div>`;

  const results = body.querySelector('.add-results');
  const showResults = (list) => {
    for (const r of list) {
      const li = document.createElement('li');
      li.className = r.ok ? 'ok' : 'err';
      li.innerHTML = icon(r.ok ? 'check' : 'alert');
      li.append(document.createTextNode(r.message || (r.ok ? 'Adicionado.' : 'Falhou.')));
      results.prepend(li);
    }
  };

  makeDropTarget(body.querySelector('.dropzone'), async (files) => showResults(await A.addFiles(files)));
  body.querySelector('[data-add="pick"]').addEventListener('click', async () => {
    const r = await A.pickFile();
    if (r) showResults([{ ok: true, message: r.message || 'Jogo adicionado.' }]);
  });

  const steamQ = body.querySelector('[data-steam-q]');
  const steamResults = body.querySelector('.steam-results');
  const search = debounce(() => searchSteamInto(steamResults, steamQ.value, { trailing: `<span class="steam-add">${icon('plus')}Adicionar</span>` }), 350);
  steamQ.addEventListener('input', search);
  steamQ.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); search.flush(); }
    if (e.key === 'ArrowDown') { const f = steamResults.querySelector('button'); if (f) { e.preventDefault(); f.focus(); } }
  });
  steamResults.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-appid]');
    if (!b || b.disabled) return;
    b.disabled = true;
    const data = await A.addSteam(b.dataset.appid, b.dataset.name);
    if (data) { b.classList.add('added'); b.querySelector('.steam-add').innerHTML = `${icon('check')}Adicionado`; }
    else b.disabled = false;
  });

  function selectTab(name) {
    for (const t of body.querySelectorAll('[data-tab]')) {
      const on = t.dataset.tab === name;
      t.setAttribute('aria-selected', String(on));
      t.tabIndex = on ? 0 : -1;
    }
    body.querySelector('#pane-file').hidden = name !== 'file';
    body.querySelector('#pane-steam').hidden = name !== 'steam';
    if (name === 'steam') steamQ.focus();
    else body.querySelector('[data-add="pick"]').focus();
  }
  body.querySelector('.tabs').addEventListener('click', (e) => { const t = e.target.closest('[data-tab]'); if (t) selectTab(t.dataset.tab); });
  body.querySelector('.tabs').addEventListener('keydown', (e) => {
    if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
      e.preventDefault();
      const cur = body.querySelector('[data-tab][aria-selected="true"]').dataset.tab;
      selectTab(cur === 'file' ? 'steam' : 'file');
      body.querySelector('[data-tab][aria-selected="true"]').focus();
    }
  });

  const m = openModal({ title: 'Adicionar jogo', body, className: 'modal-add', onClose: () => { open = null; } });
  open = { selectTab, modal: m };
  requestAnimationFrame(() => selectTab(tab));
}
