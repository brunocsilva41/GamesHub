// Library notices: "looks like the same game" suggestions (VARIANTS) and broken-shortcut cleanup,
// each a dismissible banner with a small review modal.
import { store, byName } from '../store.js';
import { icon } from '../components/icons.js';
import { esc, plural, debounce } from '../util.js';
import { openModal } from '../components/modal.js';
import * as W from '../actions2.js';

const idsKey = (ids) => [...ids].sort().join('|');

function notice(kind, ic, title, text, act) {
  return `<div class="notice notice-${kind}" role="status">` +
    `<span class="notice-ic">${icon(ic)}</span><div class="notice-text"><b>${title}</b><span>${text}</span></div>` +
    `<button type="button" class="btn btn-ghost btn-sm" data-nav data-ln="review-${act}">Revisar</button>` +
    `<button type="button" class="icon-btn" data-nav data-ln="dismiss-${act}" aria-label="Dispensar aviso" title="Dispensar">${icon('close')}</button></div>`;
}

export function initLibraryNotices(el) {
  let hideSug = '';     // suggestions key dismissed for this session
  let hideBroken = '';  // broken ids key dismissed for this session
  let lastCount = -1;
  const refresh = debounce(() => W.refreshSuggestions(), 1500);

  const brokenGames = () => store.state.games.filter((g) => g.broken);

  el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-ln]');
    if (!b) return;
    const s = store.state;
    switch (b.dataset.ln) {
      case 'review-sug': openSuggestions(); break;
      case 'dismiss-sug': hideSug = idsKey(s.suggestions.map((g) => idsKey(g.memberIds))); render(new Set(['games'])); break;
      case 'review-broken': openBroken(); break;
      case 'dismiss-broken': hideBroken = idsKey(brokenGames().map((g) => g.id)); render(new Set(['games'])); break;
    }
  });

  function render(changed) {
    const s = store.state;
    if (changed.has('status') && s.status === 'ready') W.refreshSuggestions();
    else if (changed.has('games') && s.status === 'ready' && s.games.length !== lastCount && lastCount >= 0) refresh();
    if (changed.has('games') || changed.has('status')) lastCount = s.games.length;
    if (!['games', 'suggestions', 'status', 'init'].some((k) => changed.has(k))) return;

    const parts = [];
    const sug = s.suggestions || [];
    if (sug.length && idsKey(sug.map((g) => idsKey(g.memberIds))) !== hideSug) {
      parts.push(notice('info', 'layers', 'Encontramos jogos que parecem ser o mesmo — agrupar?',
        `${plural(sug.length, 'sugestão', 'sugestões')} de variações para juntar em um só card.`, 'sug'));
    }
    const broken = brokenGames();
    if (broken.length && idsKey(broken.map((g) => g.id)) !== hideBroken) {
      parts.push(notice('warn', 'alert', plural(broken.length, 'atalho quebrado', 'atalhos quebrados'),
        'O destino não existe mais. Revise e limpe a biblioteca.', 'broken'));
    }
    const html = parts.join('');
    el.hidden = !html;
    if (el._html === html) return;
    const focused = el.contains(document.activeElement) ? document.activeElement.dataset.ln : null;
    el._html = html;
    el.innerHTML = html;
    if (focused) el.querySelector(`[data-ln="${focused}"]`)?.focus({ preventScroll: true });
  }
  return render;
}

const nameOf = (id) => store.get(id)?.name || String(id).replace(/^[a-z]+:/i, '');

/** Review modal for variantSuggestions: per group choose the primary, then accept or dismiss. */
export function openSuggestions() {
  const body = document.createElement('div');
  body.className = 'modal-body';
  const m = openModal({ title: 'Agrupar variações', body, className: 'modal-pick' });

  function render() {
    const groups = store.state.suggestions || [];
    if (!groups.length) { m.close(); return; }
    body.innerHTML = '<p class="modal-text">Cada grupo vira um só card; as outras versões ficam em “Jogar como”.</p>' +
      `<ul class="sug-list" data-arrow-nav>${groups.map((g, gi) => `<li class="sug-item" data-g="${gi}">` +
        `<ul class="sug-members">${g.memberIds.map((id) => `<li>${esc(nameOf(id))}${g.labels?.[id] ? ` <small class="muted">· ${esc(g.labels[id])}</small>` : ''}</li>`).join('')}</ul>` +
        `<div class="sug-actions"><label class="sug-primary"><span>Principal</span><span class="select-wrap"><select class="select select-sm" data-nav aria-label="Versão principal">` +
        g.memberIds.map((id) => `<option value="${esc(id)}"${id === g.primaryId ? ' selected' : ''}>${esc(nameOf(id))}</option>`).join('') +
        `</select>${icon('chevronDown')}</span></label>` +
        `<button type="button" class="btn btn-ghost btn-sm" data-nav data-sug="dismiss">Não são o mesmo jogo</button>` +
        `<button type="button" class="btn btn-primary btn-sm" data-nav data-sug="accept">${icon('layers')}Agrupar</button></div></li>`).join('')}</ul>` +
      '<div class="modal-actions"><button type="button" class="btn btn-ghost" data-nav data-sug="close">Fechar</button></div>';
    body.querySelector('.sug-item button')?.focus();
  }

  body.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-sug]');
    if (!b) return;
    if (b.dataset.sug === 'close') { m.close(); return; }
    const item = b.closest('[data-g]');
    const g = (store.state.suggestions || [])[+item.dataset.g];
    if (!g) return;
    for (const x of item.querySelectorAll('button')) x.disabled = true;
    if (b.dataset.sug === 'accept') {
      const primary = item.querySelector('select').value;
      const ok = await W.groupVariants(g.memberIds, primary);
      if (ok) store.set({ suggestions: store.state.suggestions.filter((x) => x !== g) });
    } else {
      await W.dismissVariants(g.memberIds);
    }
    render();
  });
  render();
}

/** Review modal listing broken shortcuts with reasons → cleanupBroken. */
export function openBroken() {
  const list = store.state.games.filter((g) => g.broken).sort(byName);
  if (!list.length) return;
  const body = document.createElement('div');
  body.className = 'modal-body';
  body.innerHTML = '<p class="modal-text">Estes atalhos apontam para arquivos ou pastas que não existem mais. Limpar move os atalhos para a lixeira do GamesHub (dá para desfazer); nenhum arquivo de jogo é apagado.</p>' +
    `<ul class="broken-list">${list.map((g) => `<li><b>${esc(g.name)}</b><small>${esc(g.platform)}${g.hidden ? ' · oculto' : ''}</small>` +
      `<span class="muted">${esc(g.brokenReason || 'O arquivo de destino não foi encontrado.')}</span></li>`).join('')}</ul>` +
    '<div class="modal-actions"><button type="button" class="btn btn-ghost" data-nav data-br="0">Cancelar</button>' +
    `<button type="button" class="btn btn-danger" data-nav data-br="1">${icon('trash')}Limpar biblioteca</button></div>`;
  const m = openModal({ title: plural(list.length, 'atalho quebrado', 'atalhos quebrados'), body, className: 'modal-pick', initialFocus: '[data-br="1"]' });
  body.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-br]');
    if (!b) return;
    if (b.dataset.br === '0') { m.close(); return; }
    b.disabled = true;
    b.innerHTML = '<span class="spinner" aria-hidden="true"></span>Limpando…';
    await W.cleanupBroken();
    m.close(true);
  });
}
