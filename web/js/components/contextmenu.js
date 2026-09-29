// Accessible context menu (role=menu) with one level of submenus and full keyboard support.
import { icon } from './icons.js';

let root = null;
let current = null; // { el, sub, restoreFocus }

export function initMenus(el) {
  root = el;
  document.addEventListener('mousedown', (e) => { if (current && !e.target.closest('.menu')) closeMenu(false); }, true);
  window.addEventListener('blur', () => closeMenu(false));
  window.addEventListener('resize', () => closeMenu(false));
  document.addEventListener('scroll', () => closeMenu(false), true);
}
export const menuOpen = () => !!current;

/**
 * items: [{ label, icon, action, danger, checked, disabled, submenu: items|()=>items } | { separator: true }]
 * at: { x, y } in viewport coordinates.
 */
export function openMenu(items, at) {
  closeMenu(false);
  const restoreFocus = document.activeElement;
  const el = buildMenu(items, 'menu');
  root.append(el);
  place(el, at.x, at.y);
  current = { el, sub: null, restoreFocus };
  focusItem(el, 0);
}

export function closeMenu(restore = true) {
  if (!current) return;
  const { el, sub, restoreFocus } = current;
  sub?.remove();
  el.remove();
  current = null;
  if (restore && restoreFocus?.isConnected) restoreFocus.focus({ preventScroll: true });
}

function buildMenu(items, cls) {
  const el = document.createElement('div');
  el.className = cls === 'menu' ? 'menu' : 'menu menu-sub';
  el.setAttribute('role', 'menu');
  for (const it of items) {
    if (it.separator) { el.insertAdjacentHTML('beforeend', '<div class="menu-sep" role="separator"></div>'); continue; }
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'menu-item' + (it.danger ? ' danger' : '');
    b.setAttribute('role', it.checked !== undefined ? 'menuitemcheckbox' : 'menuitem');
    if (it.checked !== undefined) b.setAttribute('aria-checked', it.checked ? 'true' : 'false');
    if (it.disabled) b.disabled = true;
    b.tabIndex = -1;
    const lead = it.checked !== undefined ? (it.checked ? icon('check') : '<span class="ico"></span>') : (it.icon ? icon(it.icon) : '<span class="ico"></span>');
    b.innerHTML = `${lead}<span class="menu-label"></span>${it.hint ? '<kbd class="menu-hint"></kbd>' : ''}${it.submenu ? icon('chevronRight', 'menu-caret') : ''}`;
    b.querySelector('.menu-label').textContent = it.label;
    if (it.hint) b.querySelector('.menu-hint').textContent = it.hint;
    if (it.submenu) {
      b.setAttribute('aria-haspopup', 'menu');
      b.addEventListener('click', () => openSub(b, it.submenu));
      b.addEventListener('mouseenter', () => openSub(b, it.submenu, false));
    } else {
      b.addEventListener('click', () => { closeMenu(); it.action?.(); });
      b.addEventListener('mouseenter', () => { if (cls === 'menu') closeSub(); b.focus(); });
    }
    el.append(b);
  }
  el.addEventListener('keydown', (e) => onKey(e, el));
  el.addEventListener('contextmenu', (e) => e.preventDefault());
  return el;
}

function openSub(anchor, submenu, focus = true) {
  if (!current) return;
  closeSub();
  const items = typeof submenu === 'function' ? submenu() : submenu;
  const sub = buildMenu(items, 'sub');
  sub._anchor = anchor;
  root.append(sub);
  const r = anchor.getBoundingClientRect();
  const w = sub.offsetWidth;
  const x = r.right + w + 4 > innerWidth ? r.left - w - 4 : r.right + 4;
  place(sub, x, r.top - 4);
  anchor.setAttribute('aria-expanded', 'true');
  current.sub = sub;
  if (focus) focusItem(sub, 0);
}

function closeSub() {
  if (!current?.sub) return;
  current.sub._anchor?.setAttribute('aria-expanded', 'false');
  current.sub.remove();
  current.sub = null;
}

function place(el, x, y) {
  const w = el.offsetWidth, hgt = el.offsetHeight;
  el.style.left = `${Math.max(8, Math.min(x, innerWidth - w - 8))}px`;
  el.style.top = `${Math.max(8, Math.min(y, innerHeight - hgt - 8))}px`;
}

const itemsOf = (menu) => Array.from(menu.querySelectorAll('.menu-item:not(:disabled)'));
function focusItem(menu, i) {
  const items = itemsOf(menu);
  if (!items.length) return;
  items[(i + items.length) % items.length].focus();
}

function onKey(e, menu) {
  const items = itemsOf(menu);
  const i = items.indexOf(document.activeElement);
  const isSub = menu.classList.contains('menu-sub');
  switch (e.key) {
    case 'ArrowDown': focusItem(menu, i + 1); break;
    case 'ArrowUp': focusItem(menu, i - 1); break;
    case 'Home': focusItem(menu, 0); break;
    case 'End': focusItem(menu, items.length - 1); break;
    case 'ArrowRight':
      if (document.activeElement?.getAttribute('aria-haspopup')) document.activeElement.click();
      break;
    case 'ArrowLeft':
      if (isSub) { const a = menu._anchor; closeSub(); a?.focus(); }
      break;
    case 'Escape':
      if (isSub) { const a = menu._anchor; closeSub(); a?.focus(); } else closeMenu();
      break;
    case 'Tab': closeMenu(); break;
    default: return;
  }
  e.preventDefault();
  e.stopPropagation();
}

/** Programmatic navigation used by the gamepad. dir: 'up'|'down'|'left'|'right' */
export function menuNavigate(dir) {
  if (!current) return;
  const menu = current.sub && current.sub.contains(document.activeElement) ? current.sub : current.el;
  const key = { up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight' }[dir];
  menu.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
}
