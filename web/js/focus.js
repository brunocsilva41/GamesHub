// Spatial (2D) focus navigation over elements marked [data-nav] inside the active scope — zone based, like a
// TV / console launcher:
//   · the screen is split into zones (sidebar, hero buttons, "Continuar jogando" shelf, toolbar, grid, …);
//   · a move first tries to stay inside the current zone (rows move sideways, columns up/down, grids in 2D);
//   · at the edge of a zone it jumps to the nearest zone in that direction, entering at the item you last used
//     there (so going up and back down returns to the same card);
//   · items scrolled out of sight inside horizontal carousels are not jump targets from other zones.

/** Zone selectors, first match wins. 'skip' zones are never reached by arrow/gamepad navigation. */
const ZONES = [
  ['.titlebar', 'skip'],
  ['.sidebar', 'column'],
  ['.hero-actions', 'row'],
  ['.shelf-row', 'row'],
  ['.bp-row', 'row'],
  ['.lib-notices', 'row'],
  ['.toolbar', 'row'],
  ['.quick-filters', 'row'],
  ['.games.grid', 'grid'],
  ['.games', 'column'],
];
const ZONE_SELECTOR = ZONES.map(([s]) => s).join(', ');

function zoneOf(el) {
  const z = el?.closest?.(ZONE_SELECTOR);
  if (!z) return null;
  const kind = ZONES.find(([s]) => z.matches(s))[1];
  return { el: z, kind };
}

function visible(el) {
  if (el.closest('[hidden]') || el.disabled) return false;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
}

/** False when a horizontally scrolling ancestor (a carousel) has the element scrolled out of its viewport. */
function inHorizontalView(el) {
  const r = el.getBoundingClientRect();
  for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
    const ox = getComputedStyle(p).overflowX;
    if (ox === 'visible' || p.scrollWidth <= p.clientWidth) continue;
    const pr = p.getBoundingClientRect();
    const shown = Math.min(r.right, pr.right) - Math.max(r.left, pr.left);
    if (shown < r.width * 0.5) return false;
  }
  return true;
}

/** The region navigation is confined to: open modal > big picture > app. */
export function navScope() {
  const modals = document.querySelectorAll('.modal');
  if (modals.length) return modals[modals.length - 1];
  const bp = document.getElementById('bigpicture');
  if (bp && !bp.hidden) return bp;
  return document.getElementById('app');
}

export function candidates(scope = navScope()) {
  return Array.from(scope.querySelectorAll('[data-nav]')).filter((el) => visible(el) && zoneOf(el)?.kind !== 'skip');
}

const center = (r) => ({ x: r.left + r.width / 2, y: r.top + r.height / 2 });

/**
 * Best element of `list` in direction `dir` from rectangle `a` (geometric, overlap on the cross axis wins).
 * With `zoneSpan`, a candidate also "overlaps" when its whole zone does (the sidebar column is beside every
 * grid row even though its items are not), so leaving sideways lands in the zone that is really beside you.
 */
function nearest(list, a, dir, exclude, zoneSpan = false) {
  const ac = center(a);
  let best = null, bestScore = Infinity;
  for (const el of list) {
    if (el === exclude) continue;
    const b = el.getBoundingClientRect();
    const bc = center(b);
    let main, cross, overlap;
    if (dir === 'left' || dir === 'right') {
      main = dir === 'right' ? b.left - a.right : a.left - b.right;
      if ((dir === 'right' ? bc.x - ac.x : ac.x - bc.x) <= 1) continue;
      cross = Math.abs(bc.y - ac.y);
      overlap = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 0;
      if (!overlap && zoneSpan) {
        const z = zoneOf(el)?.el.getBoundingClientRect();
        if (z && Math.min(a.bottom, z.bottom) - Math.max(a.top, z.top) > 0) { overlap = true; cross = Math.min(cross, 200); }
      }
    } else {
      main = dir === 'down' ? b.top - a.bottom : a.top - b.bottom;
      if ((dir === 'down' ? bc.y - ac.y : ac.y - bc.y) <= 1) continue;
      cross = Math.abs(bc.x - ac.x);
      overlap = Math.min(a.right, b.right) - Math.max(a.left, b.left) > 0;
    }
    const score = Math.max(0, main) + cross * (overlap ? 0.5 : 3) + (overlap ? 0 : 400);
    if (score < bestScore) { bestScore = score; best = el; }
  }
  return best;
}

/** Next item inside the current zone, or null at the zone's edge. */
function moveInZone(zone, from, dir, list) {
  const items = list.filter((el) => zone.el.contains(el));
  const i = items.indexOf(from);
  if (zone.kind === 'row') {
    if (dir === 'left') return items[i - 1] || null;
    if (dir === 'right') return items[i + 1] || null;
    return null;
  }
  if (zone.kind === 'column') {
    if (dir === 'up') return items[i - 1] || null;
    if (dir === 'down') return items[i + 1] || null;
    return null;
  }
  // grid: geometric, restricted to the zone
  return nearest(items, from.getBoundingClientRect(), dir, from);
}

/** Entering a zone: the item last used there if it is still around, otherwise the geometric nearest. */
function enterZone(target) {
  const z = zoneOf(target);
  const last = z?.el._lastFocus;
  if (last && last.isConnected && z.el.contains(last) && visible(last) && z.kind === 'row') return last;
  return target;
}

/**
 * Moves focus in a direction ('up'|'down'|'left'|'right'). Returns false when nothing lies that way.
 */
export function moveFocus(dir, scope = navScope()) {
  const list = candidates(scope);
  if (!list.length) return false;
  const from = document.activeElement?.closest('[data-nav]');
  if (!from || !scope.contains(from) || !list.includes(from)) {
    focusEl(firstInView(list));
    return true;
  }

  const zone = zoneOf(from);
  if (zone) {
    const inside = moveInZone(zone, from, dir, list);
    if (inside) { focusEl(inside); return true; }
  }

  // Leave the zone: nearest target outside it, measured from the focused item. Items hidden inside another
  // carousel's scroll are skipped (the carousel is entered at its remembered/visible item instead).
  const outside = list.filter((el) => (!zone || !zone.el.contains(el)) && inHorizontalView(el));
  const target = nearest(outside, from.getBoundingClientRect(), dir, from, dir === 'left' || dir === 'right');
  if (!target) return false;
  focusEl(enterZone(target));
  return true;
}

function firstInView(list) {
  return list.find((el) => {
    const r = el.getBoundingClientRect();
    return r.top >= 0 && r.bottom <= innerHeight && inHorizontalView(el) && el.closest('#main, #bigpicture, .modal');
  }) || list[0];
}

export function focusEl(el) {
  if (!el) return;
  const z = zoneOf(el);
  if (z) z.el._lastFocus = el;
  el.focus({ preventScroll: true });
  // "nearest" keeps the page still when the target is already visible and scrolls carousels just enough.
  el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
}

/** Remembers mouse/keyboard focus too, so the controller continues from wherever the user clicked. */
if (typeof document !== 'undefined') {
  document.addEventListener('focusin', (e) => {
    const el = e.target?.closest?.('[data-nav]');
    const z = el && zoneOf(el);
    if (z) z.el._lastFocus = el;
  });
}

/** The game id bound to the focused element, if any. */
export function focusedGameId() {
  return document.activeElement?.closest('[data-game]')?.dataset.game || null;
}
