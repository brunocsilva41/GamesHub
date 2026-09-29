// Input-agnostic UI commands shared by keyboard and gamepad.
import { store } from '../store.js';
import { moveFocus, focusedGameId, navScope, focusEl, candidates } from '../focus.js';
import { menuOpen, menuNavigate, closeMenu } from '../components/contextmenu.js';
import { modalOpen, closeTopModal } from '../components/modal.js';
import { sidebarSections } from '../components/sidebar.js';
import * as A from '../actions.js';

export function navigate(dir) {
  if (menuOpen()) { menuNavigate(dir); return; }
  moveFocus(dir, navScope());
}

/** A / Enter: play the focused game, or press the focused control. */
export function activate() {
  const el = document.activeElement;
  const id = focusedGameId();
  if (id && el?.matches('.card-hit, .row-hit')) {
    const g = store.get(id);
    if (g?.running) A.openDetails(id); else A.launch(id);
    return;
  }
  if (el && el !== document.body) {
    if (el.matches('input[type="checkbox"]')) { el.click(); return; }
    if (el.matches('select')) { const o = el.options; el.selectedIndex = (el.selectedIndex + 1) % o.length; el.dispatchEvent(new Event('change', { bubbles: true })); return; }
    el.click();
  } else {
    moveFocus('down');
  }
}

export function details() {
  const id = focusedGameId();
  if (!id) return;
  if (store.state.bigPicture) A.toggleBigPicture(false);
  A.openDetails(id);
}

export function favorite() {
  const id = focusedGameId() || (store.state.route.view === 'game' ? store.state.route.id : null);
  if (id) A.toggleFavorite(id);
}

export function remove() {
  const id = focusedGameId() || (store.state.route.view === 'game' ? store.state.route.id : null);
  if (id) A.removeGame(id);
}

export function contextMenu() {
  const id = focusedGameId();
  if (!id) return false;
  const r = document.activeElement.getBoundingClientRect();
  A.openGameMenu(id, { x: r.left + Math.min(24, r.width / 2), y: r.top + Math.min(r.height - 8, 48) });
  return true;
}

export function back() {
  if (menuOpen()) { closeMenu(); return true; }
  if (modalOpen()) { closeTopModal(); return true; }
  return A.back();
}

/** LB/RB: previous/next library section in sidebar order. */
export function cycleSection(delta) {
  if (store.state.bigPicture) {
    // In Big Picture, jump between shelves instead.
    const rows = Array.from(document.querySelectorAll('#bigpicture .bp-row'));
    const cur = document.activeElement?.closest('.bp-row');
    const i = Math.max(0, rows.indexOf(cur));
    const target = rows[(i + delta + rows.length) % rows.length];
    focusEl(target?.querySelector('[data-nav]'));
    return;
  }
  const keys = sidebarSections();
  if (!keys.length) return;
  const i = keys.indexOf(store.state.section);
  const next = keys[(i + delta + keys.length) % keys.length];
  A.goLibrary(next);
  requestAnimationFrame(() => {
    const first = candidates().find((el) => el.closest('#view-library .games'));
    if (first) focusEl(first);
  });
}

export const toggleBigPicture = () => A.toggleBigPicture();
export const search = () => A.focusSearch();
