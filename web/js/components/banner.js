// Update banner (shown when an `update` event arrives) with install progress.
import { store } from '../store.js';
import { icon } from './icons.js';
import * as A from '../actions.js';
import { openModal } from './modal.js';
import { notesSummary, notesNode } from './releasenotes.js';

export async function installUpdate() {
  store.set({ updatePercent: 0 });
  const data = await A.run('installUpdate');
  if (!data || !data.started) store.set({ updatePercent: null });
}

export function initBanner(el) {
  let dismissedVersion = null;
  el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-ub]');
    if (!b) return;
    const u = store.state.update;
    if (b.dataset.ub === 'install') installUpdate();
    else if (b.dataset.ub === 'notes' && u) showNotes(u);
    else if (b.dataset.ub === 'dismiss') { dismissedVersion = u?.version; render(new Set(['update'])); }
  });

  function render(changed) {
    if (!changed.has('update') && !changed.has('updatePercent') && !changed.has('init')) return;
    const { update, updatePercent } = store.state;
    const show = !!update && update.version !== dismissedVersion;
    el.hidden = !show;
    if (!show) return;
    const installing = updatePercent !== null && updatePercent !== undefined;
    if (!el.firstChild || el.dataset.version !== update.version) {
      el.dataset.version = update.version;
      el.innerHTML = `<span class="ub-ic">${icon('download')}</span>` +
        '<div class="ub-text"><strong></strong><span class="ub-notes"></span></div>' +
        '<div class="ub-progress" hidden><div class="progress"><i></i></div><span class="ub-pct"></span></div>' +
        '<div class="ub-actions">' +
        (update.notes || update.pageUrl ? '<button type="button" class="btn btn-ghost btn-sm" data-ub="notes">Novidades</button>' : '') +
        '<button type="button" class="btn btn-primary btn-sm" data-ub="install">Atualizar agora</button>' +
        `<button type="button" class="icon-btn" data-ub="dismiss" aria-label="Dispensar">${icon('close')}</button></div>`;
      el.querySelector('strong').textContent = `Nova versão disponível: ${update.version}`;
      const summary = notesSummary(update.notes);
      el.querySelector('.ub-notes').textContent = summary;
      el.querySelector('.ub-notes').title = summary;
    }
    el.querySelector('.ub-actions').hidden = installing;
    el.querySelector('.ub-progress').hidden = !installing;
    if (installing) {
      const pct = Math.max(0, Math.min(100, Math.round(updatePercent)));
      el.querySelector('.progress i').style.width = `${pct}%`;
      el.querySelector('.ub-pct').textContent = pct >= 100 ? 'Instalando…' : `Baixando… ${pct}%`;
    }
  }
  return render;
}

/** "Novidades": the release notes, formatted, in a centered dialog with the install action. */
function showNotes(u) {
  const body = document.createElement('div');
  body.className = 'modal-body rn-body';
  body.append(notesNode(u.notes));
  const foot = document.createElement('footer');
  foot.className = 'modal-foot';
  foot.innerHTML = (u.pageUrl ? '<button type="button" class="btn btn-ghost" data-rn="page">Ver no GitHub</button>' : '') +
    '<button type="button" class="btn btn-primary" data-rn="install">Atualizar agora</button>';
  const frag = document.createDocumentFragment();
  frag.append(body, foot);
  const m = openModal({ title: `Novidades da versão ${u.version}`, body: frag, className: 'modal-notes', initialFocus: '[data-rn="install"]' });
  foot.addEventListener('click', (e) => {
    const b = e.target.closest('[data-rn]');
    if (!b) return;
    if (b.dataset.rn === 'page') A.run('openExternal', { url: u.pageUrl });
    else { m.close(); installUpdate(); }
  });
}
