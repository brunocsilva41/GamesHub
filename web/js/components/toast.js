// Stackable toasts: auto-dismiss, pause on hover/focus, optional "Desfazer" action.
import { icon } from './icons.js';

const MAX = 4;
let root = null;
let undoHandler = null;

export function initToasts(el, onUndo) {
  root = el;
  undoHandler = onUndo;
}

/** toast(text, { kind: 'ok'|'err'|'info', undoToken, duration }) */
export function toast(text, opts = {}) {
  if (!root || !text) return;
  const kind = opts.kind || 'ok';
  const duration = opts.duration ?? (opts.undoToken ? 7000 : kind === 'err' ? 6000 : 3800);
  const el = document.createElement('div');
  el.className = `toast toast-${kind}`;
  el.setAttribute('role', kind === 'err' ? 'alert' : 'status');
  el.innerHTML = `<span class="toast-ic">${icon(kind === 'err' ? 'alert' : kind === 'info' ? 'info' : 'check')}</span><span class="toast-text"></span>`;
  el.querySelector('.toast-text').textContent = text;

  if (opts.undoToken && undoHandler) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'toast-undo';
    b.textContent = 'Desfazer';
    b.addEventListener('click', () => { undoHandler(opts.undoToken); dismiss(); });
    el.append(b);
  }
  const x = document.createElement('button');
  x.type = 'button';
  x.className = 'toast-close';
  x.setAttribute('aria-label', 'Fechar notificação');
  x.innerHTML = icon('close');
  x.addEventListener('click', () => dismiss());
  el.append(x);

  let remaining = duration, started = Date.now(), timer = null;
  const start = () => { started = Date.now(); timer = setTimeout(dismiss, remaining); };
  const pause = () => { clearTimeout(timer); remaining = Math.max(1200, remaining - (Date.now() - started)); };
  el.addEventListener('mouseenter', pause);
  el.addEventListener('mouseleave', start);
  el.addEventListener('focusin', pause);
  el.addEventListener('focusout', start);

  let gone = false;
  function dismiss() {
    if (gone) return;
    gone = true;
    clearTimeout(timer);
    el.classList.add('leaving');
    setTimeout(() => el.remove(), 200);
  }

  root.append(el);
  while (root.children.length > MAX) root.firstElementChild.remove();
  start();
  return dismiss;
}
