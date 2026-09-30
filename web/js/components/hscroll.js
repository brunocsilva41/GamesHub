// Custom horizontal scroll for carousels (e.g. "Continuar jogando"): the native scrollbar is hidden and replaced
// by a thin themed track with a draggable thumb, page buttons (◀ ▶) and edge fades that show there is more
// content. Keyboard/gamepad focus keeps working: focused cards are scrolled into view by focusEl().
import { icon } from './icons.js';

/**
 * Enhances `scroller` (an element with overflow-x: auto). `controls` (optional) receives the ◀ ▶ buttons,
 * e.g. the section header; otherwise they are placed over the carousel edges.
 * Returns { update } to call after the content changes.
 */
export function enhanceHScroll(scroller, { controls = null, label = 'itens' } = {}) {
  const wrap = document.createElement('div');
  wrap.className = 'hscroll';
  scroller.parentNode.insertBefore(wrap, scroller);
  wrap.append(scroller);
  scroller.classList.add('hscroll-view');

  const track = document.createElement('div');
  track.className = 'hscroll-track';
  track.setAttribute('aria-hidden', 'true');
  track.innerHTML = '<div class="hscroll-thumb"></div>';
  wrap.append(track);
  const thumb = track.firstElementChild;

  const nav = document.createElement('div');
  nav.className = 'hscroll-nav' + (controls ? '' : ' hscroll-nav-overlay');
  nav.innerHTML =
    `<button type="button" class="hscroll-btn" data-dir="-1" aria-label="Rolar ${label} para a esquerda">${icon('chevronLeft')}</button>` +
    `<button type="button" class="hscroll-btn" data-dir="1" aria-label="Rolar ${label} para a direita">${icon('chevronRight')}</button>`;
  (controls || wrap).append(nav);
  const [prevBtn, nextBtn] = nav.querySelectorAll('button');

  const reduce = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
  const max = () => Math.max(0, scroller.scrollWidth - scroller.clientWidth);

  let raf = 0;
  function update() {
    raf = 0;
    const m = max();
    const overflow = m > 1;
    wrap.classList.toggle('has-overflow', overflow);
    nav.hidden = !overflow;
    if (!overflow) return;
    const x = scroller.scrollLeft;
    const trackW = track.clientWidth;
    const w = Math.max(32, trackW * (scroller.clientWidth / scroller.scrollWidth));
    thumb.style.width = `${w}px`;
    thumb.style.transform = `translateX(${(trackW - w) * (x / m)}px)`;
    const atStart = x <= 1, atEnd = x >= m - 1;
    prevBtn.disabled = atStart;
    nextBtn.disabled = atEnd;
    wrap.classList.toggle('fade-start', !atStart);
    wrap.classList.toggle('fade-end', !atEnd);
  }
  const schedule = () => { if (!raf) raf = requestAnimationFrame(update); };

  // Page buttons: about one visible width, landing on whole cards thanks to scroll-snap.
  nav.addEventListener('click', (e) => {
    const b = e.target.closest('.hscroll-btn');
    if (!b) return;
    scroller.scrollBy({ left: Number(b.dataset.dir) * scroller.clientWidth * 0.85, behavior: reduce() ? 'auto' : 'smooth' });
  });

  // Thumb drag (pointer capture: keeps working when the pointer leaves the track).
  let drag = null;
  thumb.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    e.preventDefault();
    thumb.setPointerCapture(e.pointerId);
    drag = { x: e.clientX, start: scroller.scrollLeft };
    wrap.classList.add('dragging');
  });
  thumb.addEventListener('pointermove', (e) => {
    if (!drag) return;
    const free = track.clientWidth - thumb.offsetWidth;
    if (free <= 0) return;
    scroller.scrollLeft = drag.start + (e.clientX - drag.x) * (max() / free);
  });
  const endDrag = () => { drag = null; wrap.classList.remove('dragging'); };
  thumb.addEventListener('pointerup', endDrag);
  thumb.addEventListener('pointercancel', endDrag);

  // Click on the track: center the thumb there.
  track.addEventListener('pointerdown', (e) => {
    if (e.target !== track) return;
    const r = track.getBoundingClientRect();
    const free = track.clientWidth - thumb.offsetWidth;
    const pos = Math.min(Math.max(0, e.clientX - r.left - thumb.offsetWidth / 2), free);
    scroller.scrollTo({ left: free > 0 ? (pos / free) * max() : 0, behavior: reduce() ? 'auto' : 'smooth' });
  });

  // Vertical mouse wheel over the carousel scrolls it sideways (only while it can still move that way,
  // so the page keeps scrolling normally at the ends).
  scroller.addEventListener('wheel', (e) => {
    if (e.ctrlKey || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
    const m = max();
    if (m <= 1) return;
    const x = scroller.scrollLeft;
    if ((e.deltaY < 0 && x <= 1) || (e.deltaY > 0 && x >= m - 1)) return;
    e.preventDefault();
    scroller.scrollLeft = x + e.deltaY;
  }, { passive: false });

  scroller.addEventListener('scroll', schedule, { passive: true });
  new ResizeObserver(schedule).observe(scroller);
  new MutationObserver(schedule).observe(scroller, { childList: true });
  schedule();
  return { update: schedule };
}
