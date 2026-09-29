// Delegated mouse handling for any container of game cards/rows.
import { store } from '../store.js';
import * as A from '../actions.js';

export function bindGameEvents(container) {
  container.addEventListener('click', (e) => {
    const b = e.target.closest('[data-action]');
    const card = e.target.closest('[data-game]');
    if (!b || !card || !container.contains(card)) return;
    const id = card.dataset.game;
    switch (b.dataset.action) {
      case 'open': {
        const g = store.get(id);
        if (g?.running) A.openDetails(id); else A.launch(id);
        break;
      }
      case 'fav': A.toggleFavorite(id); break;
      case 'details': A.openDetails(id); break;
    }
  });
  container.addEventListener('contextmenu', (e) => {
    const card = e.target.closest('[data-game]');
    if (!card) return;
    e.preventDefault();
    card.querySelector('[data-nav]')?.focus({ preventScroll: true });
    A.openGameMenu(card.dataset.game, { x: e.clientX, y: e.clientY });
  });
}
