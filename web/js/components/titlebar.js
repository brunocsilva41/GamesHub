// Custom title bar: drag region, back, sidebar toggle, Big Picture, pin, min/max/close.
import { store } from '../store.js';
import { icon } from './icons.js';
import * as A from '../actions.js';

export function initTitlebar(el) {
  el.innerHTML = `
    <div class="tb-left">
      <button type="button" class="tb-btn" data-tb="sidebar" aria-label="Recolher barra lateral" title="Recolher barra lateral">${icon('sidebar')}</button>
      <button type="button" class="tb-btn" data-tb="back" aria-label="Voltar" title="Voltar (Esc)" hidden>${icon('back')}</button>
      <img class="tb-logo" src="logo.png" alt="" width="18" height="18">
      <span class="tb-name">GamesHub</span>
      <span class="tb-demo" hidden title="A interface está usando dados de exemplo">Modo demonstração</span>
    </div>
    <div class="tb-drag" aria-hidden="true"></div>
    <div class="tb-right">
      <button type="button" class="tb-btn" data-tb="bigpicture" aria-label="Modo tela cheia (Big Picture)" title="Modo tela cheia (F11)">${icon('tv')}</button>
      <button type="button" class="tb-btn" data-tb="pin" aria-pressed="false" aria-label="Manter janela no topo" title="Manter no topo">${icon('pin')}</button>
      <span class="tb-sep"></span>
      <button type="button" class="tb-btn tb-win" data-tb="minimize" aria-label="Minimizar" title="Minimizar">${icon('minimize')}</button>
      <button type="button" class="tb-btn tb-win" data-tb="maximize" aria-label="Maximizar" title="Maximizar">${icon('maximize')}</button>
      <button type="button" class="tb-btn tb-win tb-close" data-tb="close" aria-label="Fechar" title="Fechar">${icon('close')}</button>
    </div>`;

  el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-tb]');
    if (!b) return;
    const ws = store.state.windowState;
    switch (b.dataset.tb) {
      case 'sidebar': store.set({ sidebarCollapsed: !store.state.sidebarCollapsed }); break;
      case 'back': A.back(); break;
      case 'bigpicture': A.toggleBigPicture(true); break;
      case 'pin': A.windowAction('pin', !ws.pinned); break;
      case 'minimize': A.windowAction('minimize'); break;
      case 'maximize': A.windowAction(ws.maximized ? 'restore' : 'maximize'); break;
      case 'close': A.windowAction('close'); break;
    }
  });
  // With WebView2 non-client regions the OS usually handles this; kept for hosts that forward it.
  el.addEventListener('dblclick', (e) => {
    if (e.target.closest('button, input, a')) return;
    A.windowAction(store.state.windowState.maximized ? 'restore' : 'maximize');
  });

  const q = (s) => el.querySelector(s);
  return function render(changed) {
    const s = store.state;
    if (changed.has('windowState') || changed.has('init')) {
      const max = q('[data-tb="maximize"]');
      max.innerHTML = icon(s.windowState.maximized ? 'restore' : 'maximize');
      max.setAttribute('aria-label', s.windowState.maximized ? 'Restaurar' : 'Maximizar');
      max.title = s.windowState.maximized ? 'Restaurar' : 'Maximizar';
      const pin = q('[data-tb="pin"]');
      pin.setAttribute('aria-pressed', s.windowState.pinned ? 'true' : 'false');
      pin.classList.toggle('on', !!s.windowState.pinned);
      pin.title = s.windowState.pinned ? 'Não manter no topo' : 'Manter no topo';
    }
    if (changed.has('route') || changed.has('init')) q('[data-tb="back"]').hidden = s.route.view === 'library';
    if (changed.has('sidebarCollapsed') || changed.has('init')) {
      const b = q('[data-tb="sidebar"]');
      const label = s.sidebarCollapsed ? 'Expandir barra lateral' : 'Recolher barra lateral';
      b.setAttribute('aria-label', label); b.title = label;
      b.setAttribute('aria-expanded', s.sidebarCollapsed ? 'false' : 'true');
    }
    if (changed.has('demo') || changed.has('init')) q('.tb-demo').hidden = !s.demo;
  };
}
