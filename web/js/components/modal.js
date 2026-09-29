// Focus-trapped dialogs: openModal() for custom content, confirm() / prompt() helpers.
import { icon } from './icons.js';

let root = null;
const stack = [];

export function initModals(el) { root = el; }
export const modalOpen = () => stack.length > 0;

const FOCUSABLE = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * opts: { title, body: Node, className, onClose(result), initialFocus: selector, dismissable = true }
 * Returns { el, close(result) }.
 */
export function openModal(opts) {
  const previous = document.activeElement;
  const backdrop = document.createElement('div');
  backdrop.className = 'modal-backdrop';
  const dlg = document.createElement('div');
  dlg.className = `modal ${opts.className || ''}`.trim();
  dlg.setAttribute('role', opts.role || 'dialog');
  dlg.setAttribute('aria-modal', 'true');
  const titleId = `mt${Date.now().toString(36)}${stack.length}`;
  dlg.setAttribute('aria-labelledby', titleId);
  dlg.innerHTML = `<header class="modal-head"><h2 id="${titleId}" class="modal-title"></h2>` +
    `<button type="button" class="icon-btn modal-x" aria-label="Fechar">${icon('close')}</button></header>`;
  dlg.querySelector('.modal-title').textContent = opts.title || '';
  if (opts.body) dlg.append(opts.body);
  backdrop.append(dlg);

  let closed = false;
  const api = {
    el: dlg,
    close(result) {
      if (closed) return;
      closed = true;
      const i = stack.indexOf(api);
      if (i >= 0) stack.splice(i, 1);
      backdrop.classList.add('leaving');
      setTimeout(() => backdrop.remove(), 150);
      document.body.classList.toggle('has-modal', stack.length > 0);
      if (previous && previous.isConnected) previous.focus({ preventScroll: true });
      opts.onClose?.(result);
    },
    dismiss() { if (opts.dismissable !== false) api.close(undefined); },
  };

  dlg.querySelector('.modal-x').addEventListener('click', () => api.dismiss());
  backdrop.addEventListener('mousedown', (e) => { if (e.target === backdrop) api.dismiss(); });
  dlg.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); api.dismiss(); return; }
    if (e.key !== 'Tab') return;
    const items = Array.from(dlg.querySelectorAll(FOCUSABLE)).filter((n) => n.offsetParent !== null);
    if (!items.length) { e.preventDefault(); return; }
    const first = items[0], last = items[items.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  });

  stack.push(api);
  root.append(backdrop);
  document.body.classList.add('has-modal');
  requestAnimationFrame(() => {
    const target = (opts.initialFocus && dlg.querySelector(opts.initialFocus)) ||
      dlg.querySelector('.modal-body ' + FOCUSABLE) || dlg.querySelector(FOCUSABLE);
    target?.focus();
  });
  return api;
}

export function closeTopModal() {
  const top = stack[stack.length - 1];
  if (top) { top.dismiss(); return true; }
  return false;
}

/** Resolves true/false. opts: { title, message, confirmLabel, danger } */
export function confirm({ title, message, confirmLabel = 'Confirmar', cancelLabel = 'Cancelar', danger = false }) {
  return new Promise((resolve) => {
    const body = document.createElement('div');
    body.className = 'modal-body';
    body.innerHTML = '<p class="modal-text"></p><div class="modal-actions">' +
      `<button type="button" class="btn btn-ghost" data-r="0"></button>` +
      `<button type="button" class="btn ${danger ? 'btn-danger' : 'btn-primary'}" data-r="1"></button></div>`;
    body.querySelector('.modal-text').textContent = message;
    body.querySelector('[data-r="0"]').textContent = cancelLabel;
    body.querySelector('[data-r="1"]').textContent = confirmLabel;
    const m = openModal({
      title, body, role: 'alertdialog', className: 'modal-sm', initialFocus: '[data-r="1"]',
      onClose: (r) => resolve(r === true),
    });
    body.addEventListener('click', (e) => {
      const b = e.target.closest('[data-r]');
      if (b) m.close(b.dataset.r === '1');
    });
  });
}

/** Resolves the trimmed text or null. */
export function prompt({ title, label, placeholder = '', value = '', confirmLabel = 'OK' }) {
  return new Promise((resolve) => {
    const body = document.createElement('form');
    body.className = 'modal-body';
    body.innerHTML = '<label class="field"><span class="field-label"></span><input class="input" type="text" spellcheck="false"></label>' +
      '<div class="modal-actions"><button type="button" class="btn btn-ghost" data-r="0">Cancelar</button>' +
      '<button type="submit" class="btn btn-primary"></button></div>';
    body.querySelector('.field-label').textContent = label || '';
    const input = body.querySelector('input');
    input.placeholder = placeholder;
    input.value = value;
    body.querySelector('[type="submit"]').textContent = confirmLabel;
    const m = openModal({ title, body, className: 'modal-sm', initialFocus: 'input', onClose: (r) => resolve(r ?? null) });
    body.querySelector('[data-r="0"]').addEventListener('click', () => m.close(null));
    body.addEventListener('submit', (e) => { e.preventDefault(); const v = input.value.trim(); m.close(v || null); });
  });
}
