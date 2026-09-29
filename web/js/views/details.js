// Game details: hero + logo, play, stats, collections, edit form (with Steam search), images, actions.
import { store } from '../store.js';
import { createHero, updateHero, playButtonHtml } from '../components/hero.js';
import { icon } from '../components/icons.js';
import { esc, fmtDuration, fmtDate, fmtRelative, fmtBytes, SOURCE_LABELS } from '../util.js';
import { renderArtSlots, updateArtSlots, bindArtSlots } from './artslots.js';
import { initEditForm } from './editform.js';
import { toast } from '../components/toast.js';
import * as A from '../actions.js';

export function initDetails(root) {
  let currentId = null;
  let edit = null;

  root.addEventListener('click', (e) => {
    const b = e.target.closest('[data-hero], [data-d]');
    if (!b || !currentId) return;
    const id = currentId;
    const g = store.get(id);
    const act = b.dataset.hero || b.dataset.d;
    switch (act) {
      case 'play': A.launch(id); break;
      case 'variants': A.openVariantMenu(id, b); break;
      case 'fav': A.toggleFavorite(id); break;
      case 'reveal': A.reveal(id); break;
      case 'hide': A.setHidden(id, !g?.hidden); break;
      case 'remove': A.removeGame(id); break;
      case 'menu': {
        const r = b.getBoundingClientRect();
        A.openGameMenu(id, { x: r.left, y: r.bottom + 4 });
        break;
      }
      case 'col': A.toggleCollection(id, b.dataset.col); break;
      case 'refresh-art':
        A.run('refreshArt', { id }).then((d) => { if (d) toast(d.message || 'Buscando artes…', { kind: 'info' }); });
        break;
    }
  });
  root.addEventListener('submit', (e) => {
    if (!e.target.matches('.col-new')) return;
    e.preventDefault();
    const input = e.target.querySelector('input');
    const name = input.value.trim();
    if (!name || !currentId) return;
    const g = store.get(currentId);
    if (!(g.collections || []).includes(name)) A.toggleCollection(currentId, name);
    input.value = '';
  });

  function build(g) {
    root.innerHTML = '';
    const hero = createHero('hero-details');
    root.append(hero);
    const body = document.createElement('div');
    body.className = 'details-body';
    body.innerHTML = `
      <div class="details-main">
        <section class="panel panel-warn" data-broken hidden>${icon('alert')}<div><b>Atalho quebrado.</b> <span data-broken-reason></span></div></section>
        <section class="panel" aria-labelledby="d-stats"><h2 class="panel-title" id="d-stats">Informações</h2><dl class="stats"></dl></section>
        <section class="panel" aria-labelledby="d-cols">
          <h2 class="panel-title" id="d-cols">Coleções</h2>
          <div class="chips" role="group" aria-label="Coleções do jogo"></div>
          <form class="col-new" autocomplete="off">
            <input class="input input-sm" type="text" maxlength="40" placeholder="Nova coleção…" aria-label="Nome da nova coleção" spellcheck="false">
            <button type="submit" class="btn btn-ghost btn-sm">${icon('plus')}Criar</button>
          </form>
        </section>
        <section class="panel edit-panel" aria-labelledby="d-edit"><h2 class="panel-title" id="d-edit">Editar</h2></section>
        <section class="panel" aria-labelledby="d-art">
          <div class="panel-head"><h2 class="panel-title" id="d-art">Imagens</h2>
            <button type="button" class="btn btn-ghost btn-sm" data-nav data-d="refresh-art">${icon('refresh')}Buscar artes novamente</button></div>
          <p class="panel-hint">Arraste uma imagem sobre um espaço ou clique para escolher um arquivo.</p>
          <div class="art-slots"></div>
        </section>
      </div>
      <aside class="details-side">
        <section class="panel">
          <h2 class="panel-title">Ações</h2>
          <div class="side-actions">
            <button type="button" class="btn btn-ghost btn-block" data-nav data-d="reveal">${icon('folder')}Abrir local</button>
            <button type="button" class="btn btn-ghost btn-block" data-nav data-d="hide"></button>
            <button type="button" class="btn btn-danger-ghost btn-block" data-nav data-d="remove">${icon('trash')}Remover…</button>
          </div>
        </section>
        <section class="panel"><h2 class="panel-title">Arquivo</h2><p class="path-text"></p></section>
      </aside>`;
    root.append(body);
    body.querySelector('.art-slots').innerHTML = renderArtSlots();
    bindArtSlots(body.querySelector('.art-slots'), () => currentId);
    edit = initEditForm(body.querySelector('.edit-panel'), () => currentId);
    root.scrollTop = 0;
  }

  function update(g) {
    const hero = root.querySelector('.hero');
    updateHero(hero, g, {
      eyebrow: g.hidden ? 'Oculto' : '',
      actionsHtml: playButtonHtml(g) +
        `<button type="button" class="btn btn-glass btn-lg${g.favorite ? ' is-on' : ''}" data-nav data-hero="fav" aria-pressed="${g.favorite}">` +
        `${icon(g.favorite ? 'starFill' : 'star')}${g.favorite ? 'Favorito' : 'Favoritar'}</button>` +
        `<button type="button" class="btn btn-glass btn-lg btn-icon" data-nav data-d="menu" aria-label="Mais ações" title="Mais ações">${icon('more')}</button>`,
    });

    const stats = [
      ['Tempo jogado', g.playSeconds ? fmtDuration(g.playSeconds) : 'Nenhum'],
      ['Última vez', g.running ? 'Agora (em execução)' : g.lastPlayed ? `${fmtRelative(g.lastPlayed)} · ${fmtDate(g.lastPlayed)}` : 'Nunca jogado'],
      ['Adicionado em', fmtDate(g.addedAt)],
      ['Plataforma', g.platform],
      ['Origem', SOURCE_LABELS[g.source] || g.source],
    ];
    if (g.sizeBytes) stats.push(['Tamanho', fmtBytes(g.sizeBytes)]);
    if (g.genres?.length) stats.push(['Gêneros', g.genres.join(', ')]);
    if (g.steamAppId) stats.push(['Steam App ID', g.steamAppId]);
    const statsHtml = stats.map(([k, v]) => `<div class="stat"><dt>${k}</dt><dd>${esc(v)}</dd></div>`).join('');
    const statsEl = root.querySelector('.stats');
    if (statsEl._html !== statsHtml) { statsEl._html = statsHtml; statsEl.innerHTML = statsHtml; }

    const names = [...new Set([...store.state.collections, ...(g.collections || [])])];
    const chipsHtml = names.length
      ? names.map((n) => {
        const on = (g.collections || []).includes(n);
        return `<button type="button" class="chip-toggle${on ? ' on' : ''}" data-nav data-d="col" data-col="${esc(n)}" aria-pressed="${on}">${on ? icon('check') : icon('plus')}${esc(n)}</button>`;
      }).join('')
      : '<span class="muted">Nenhuma coleção ainda. Crie a primeira abaixo.</span>';
    const chips = root.querySelector('.chips');
    if (chips._html !== chipsHtml) {
      const focused = chips.contains(document.activeElement) ? document.activeElement.dataset.col : null;
      chips._html = chipsHtml; chips.innerHTML = chipsHtml;
      if (focused) chips.querySelector(`[data-col="${CSS.escape(focused)}"]`)?.focus({ preventScroll: true });
    }

    const broken = root.querySelector('[data-broken]');
    broken.hidden = !g.broken;
    broken.querySelector('[data-broken-reason]').textContent = g.brokenReason || 'O arquivo de destino não foi encontrado. Edite o atalho ou remova o jogo.';

    const hide = root.querySelector('[data-d="hide"]');
    const hideHtml = g.hidden ? `${icon('eye')}Mostrar na biblioteca` : `${icon('eyeOff')}Ocultar`;
    if (hide._html !== hideHtml) { hide._html = hideHtml; hide.innerHTML = hideHtml; }
    root.querySelector('.path-text').textContent = g.filePath || g.installDir || (g.source === 'steam' ? `steam://rungameid/${g.steamAppId || g.id.slice(6)}` : '—');

    updateArtSlots(root.querySelector('.art-slots'), g);
    edit.update(g);
  }

  return function render(changed) {
    const s = store.state;
    if (s.route.view !== 'game') { currentId = null; return; }
    if (!changed.has('route') && !changed.has('games') && !changed.has('collections') && !changed.has('init')) return;
    const g = store.get(s.route.id);
    if (!g) {
      if (s.status === 'ready') queueMicrotask(() => A.goLibrary());
      return;
    }
    if (currentId !== g.id) {
      currentId = g.id;
      build(g);
      update(g);
      requestAnimationFrame(() => root.querySelector('[data-hero="play"]')?.focus({ preventScroll: true }));
    } else {
      update(g);
    }
  };
}
