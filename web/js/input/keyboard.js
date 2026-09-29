// Keyboard shortcuts and 2D arrow navigation.
//   Arrows: move focus · Enter: jogar · I / Shift+Enter: detalhes · F: favoritar · Delete: remover
//   Ctrl+F or /: buscar · Ctrl+N: adicionar · Ctrl+,: configurações · F11: Big Picture
//   Esc: voltar/fechar · F5: reescanear · Shift+F10 / ContextMenu: menu do jogo
import { store } from '../store.js';
import { isTextInput } from '../util.js';
import { modalOpen } from '../components/modal.js';
import { menuOpen } from '../components/contextmenu.js';
import { focusedGameId } from '../focus.js';
import { openAddGame } from '../views/addgame.js';
import * as C from './commands.js';
import * as A from '../actions.js';

const ARROWS = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' };

export function initKeyboard(onUserInput) {
  document.addEventListener('keydown', (e) => {
    onUserInput?.();
    const ctrl = e.ctrlKey || e.metaKey;
    const k = e.key;
    const typing = isTextInput(e.target);

    // Global shortcuts that work everywhere (including text fields).
    if (k === 'F11') { e.preventDefault(); C.toggleBigPicture(); return; }
    if (k === 'F5') { e.preventDefault(); if (!e.repeat) A.rescan(); return; }
    if (ctrl && !e.altKey && k.toLowerCase() === 'f') { e.preventDefault(); if (!modalOpen()) C.search(); return; }
    if (ctrl && k.toLowerCase() === 'n') { e.preventDefault(); if (!modalOpen()) openAddGame(); return; }
    if (ctrl && (k === ',' || e.code === 'Comma')) { e.preventDefault(); if (!modalOpen()) { if (store.state.bigPicture) A.toggleBigPicture(false); A.openSettings(); } return; }
    if (ctrl && ['r', 'p', 'u', 's', 'g', 'j', 'h'].includes(k.toLowerCase())) { e.preventDefault(); return; } // browser leftovers
    if (menuOpen()) return; // the menu handles its own keys

    if (k === 'Escape') {
      if (typing) { e.target.blur(); e.preventDefault(); return; }
      if (C.back()) e.preventDefault();
      return;
    }
    if (typing) return;
    if (modalOpen()) {
      if (ARROWS[k] && e.target.closest?.('.steam-results, .modal-actions')) { e.preventDefault(); C.navigate(ARROWS[k]); }
      return;
    }

    if (ARROWS[k] && !e.altKey && !ctrl) {
      if (e.target.tagName === 'SELECT' && (k === 'ArrowUp' || k === 'ArrowDown')) return;
      e.preventDefault();
      C.navigate(ARROWS[k]);
      return;
    }
    if (k === '/' && !ctrl) { e.preventDefault(); C.search(); return; }
    if (k === 'ContextMenu' || (e.shiftKey && k === 'F10')) { if (C.contextMenu()) e.preventDefault(); return; }

    const onGame = !!focusedGameId();
    if (k === 'Enter' && e.shiftKey && onGame) { e.preventDefault(); C.details(); return; }
    if (k === 'Enter' && onGame) { e.preventDefault(); if (!e.repeat) C.activate(); return; }
    if (ctrl || e.altKey) return;
    const lower = k.toLowerCase();
    if (lower === 'i' && onGame) { e.preventDefault(); C.details(); return; }
    if (lower === 'f' && (onGame || store.state.route.view === 'game')) { e.preventDefault(); C.favorite(); return; }
    if (k === 'Delete' && (onGame || store.state.route.view === 'game')) { e.preventDefault(); C.remove(); return; }
  });

  // No browser context menu outside cards and text fields.
  document.addEventListener('contextmenu', (e) => {
    if (!e.target.closest('[data-game], input, textarea')) e.preventDefault();
  });
}
