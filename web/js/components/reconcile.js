// Keyed DOM reconciliation: reuses elements by key, patches them, inserts new ones, removes stale ones,
// and moves nodes only when the order actually changed. Keeps keyboard focus on the same item.

export function reconcile(container, items, { key, create, update }) {
  const active = document.activeElement;
  const focusedItem = active && container.contains(active) ? active.closest('[data-key]') : null;
  const focusedKey = focusedItem?.dataset.key ?? null;
  const focusSelector = focusedItem && active !== focusedItem && active.dataset.focusRole
    ? `[data-focus-role="${active.dataset.focusRole}"]` : null;

  const keys = items.map(key);
  const wanted = new Set(keys);
  const existing = new Map();
  for (const el of Array.from(container.children)) {
    const k = el.dataset.key;
    if (k === undefined) continue;
    if (!wanted.has(k) || existing.has(k)) el.remove();
    else existing.set(k, el);
  }

  let cursor = container.firstElementChild;
  items.forEach((item, i) => {
    const k = keys[i];
    let el = existing.get(k);
    if (el) update(el, item);
    else { el = create(item); el.dataset.key = k; }
    if (el === cursor) cursor = cursor.nextElementSibling;
    else container.insertBefore(el, cursor);
  });

  if (focusedKey !== null && document.activeElement !== active) {
    const el = container.querySelector(`[data-key="${CSS.escape(focusedKey)}"]`);
    const target = el && (focusSelector ? el.querySelector(focusSelector) : el.querySelector('[data-nav]')) || el;
    target?.focus({ preventScroll: true });
  }
}
