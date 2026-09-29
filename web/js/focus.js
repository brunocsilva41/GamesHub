// Spatial (2D) focus navigation over elements marked [data-nav] inside the active scope.

function visible(el) {
  if (el.closest('[hidden]') || el.disabled) return false;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
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
  return Array.from(scope.querySelectorAll('[data-nav]')).filter(visible);
}

/**
 * Moves focus to the nearest candidate in a direction ('up'|'down'|'left'|'right').
 * Prefers elements that overlap on the perpendicular axis (true grid rows/columns).
 */
export function moveFocus(dir, scope = navScope()) {
  const list = candidates(scope);
  if (!list.length) return false;
  const from = document.activeElement?.closest('[data-nav]');
  if (!from || !scope.contains(from) || !list.includes(from)) {
    focusEl(firstInView(list));
    return true;
  }
  const a = from.getBoundingClientRect();
  const ac = { x: a.left + a.width / 2, y: a.top + a.height / 2 };
  let best = null, bestScore = Infinity;
  for (const el of list) {
    if (el === from) continue;
    const b = el.getBoundingClientRect();
    const bc = { x: b.left + b.width / 2, y: b.top + b.height / 2 };
    let main, cross, overlap;
    if (dir === 'left' || dir === 'right') {
      main = dir === 'right' ? b.left - a.right : a.left - b.right;
      if ((dir === 'right' ? bc.x - ac.x : ac.x - bc.x) <= 1) continue;
      cross = Math.abs(bc.y - ac.y);
      overlap = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 0;
    } else {
      main = dir === 'down' ? b.top - a.bottom : a.top - b.bottom;
      if ((dir === 'down' ? bc.y - ac.y : ac.y - bc.y) <= 1) continue;
      cross = Math.abs(bc.x - ac.x);
      overlap = Math.min(a.right, b.right) - Math.max(a.left, b.left) > 0;
    }
    // Overlapping candidates (same row/column) always beat diagonal ones.
    const score = Math.max(0, main) + cross * (overlap ? 0.5 : 3) + (overlap ? 0 : 400);
    if (score < bestScore) { bestScore = score; best = el; }
  }
  if (!best) return false;
  focusEl(best);
  return true;
}

function firstInView(list) {
  return list.find((el) => {
    const r = el.getBoundingClientRect();
    return r.top >= 0 && r.bottom <= innerHeight && el.closest('#main, #bigpicture, .modal');
  }) || list[0];
}

export function focusEl(el) {
  if (!el) return;
  el.focus({ preventScroll: true });
  el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'auto' });
}

/** The game id bound to the focused element, if any. */
export function focusedGameId() {
  return document.activeElement?.closest('[data-game]')?.dataset.game || null;
}
