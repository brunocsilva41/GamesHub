// Artwork box with a fallback chain: image sources in order -> icon on a deterministic gradient -> monogram.
// Images are lazy-loaded and fade in once; updates with identical sources are no-ops.
import { gradientFor, monogram } from '../util.js';

/** Ordered image candidates for a card style. */
export function cardSources(art, style) {
  const a = art || {};
  return style === 'portrait' ? [a.capsule, a.header, a.hero] : [a.header, a.hero, a.capsule];
}

export function artBox(extraClass = '', { lazy = true } = {}) {
  const box = document.createElement('span');
  box.className = `art ${extraClass}`.trim();
  box.innerHTML = '<span class="art-mono" aria-hidden="true"></span>' +
    '<img class="art-icon" alt="" decoding="async" hidden>' +
    `<img class="art-img" alt="" decoding="async"${lazy ? ' loading="lazy"' : ''} hidden>`;
  return box;
}

/**
 * Applies artwork to a box created by artBox().
 * opts: { sources: string[], icon: string|null, name, seed }
 */
export function setArt(box, { sources = [], icon = null, name = '', seed = '' }) {
  const list = sources.filter(Boolean);
  const key = JSON.stringify([list, icon, name, seed]);
  if (box._artKey === key) return;
  const firstTime = box._artKey === undefined;
  box._artKey = key;
  const token = (box._artToken = (box._artToken || 0) + 1);

  const mono = box.querySelector('.art-mono');
  const img = box.querySelector('.art-img');
  const ico = box.querySelector('.art-icon');
  box.style.setProperty('--art-bg', gradientFor(seed || name));
  mono.textContent = monogram(name);

  const showIcon = () => {
    if (!icon) { ico.hidden = true; ico.removeAttribute('src'); box.classList.remove('has-icon'); return; }
    ico.onload = () => { if (box._artToken === token) { ico.hidden = false; box.classList.add('has-icon'); } };
    ico.onerror = () => { if (box._artToken === token) { ico.hidden = true; box.classList.remove('has-icon'); } };
    ico.src = icon;
  };

  const clearImg = () => {
    img.hidden = true;
    img.classList.remove('loaded');
    img.removeAttribute('src');
    box.classList.remove('has-img');
  };

  const tryAt = (i) => {
    if (box._artToken !== token) return;
    if (i >= list.length) { clearImg(); showIcon(); return; }
    const url = list[i];
    const hadImage = box.classList.contains('has-img') && !firstTime;
    if (hadImage) {
      // Keep the current picture until the replacement is ready (no flash on art updates).
      const pre = new Image();
      pre.onload = () => {
        if (box._artToken !== token) return;
        img.src = url;
        ico.hidden = true; box.classList.remove('has-icon');
      };
      pre.onerror = () => tryAt(i + 1);
      pre.src = url;
      return;
    }
    img.onload = () => {
      if (box._artToken !== token) return;
      img.hidden = false;
      box.classList.add('has-img');
      ico.hidden = true; box.classList.remove('has-icon');
      requestAnimationFrame(() => img.classList.add('loaded'));
    };
    img.onerror = () => { if (box._artToken === token) tryAt(i + 1); };
    img.classList.remove('loaded');
    img.hidden = false; // must be rendered for loading="lazy" to kick in
    img.src = url;
  };

  if (list.length) {
    if (!box.classList.contains('has-img')) showIcon(); // icon/monogram while the image loads
    tryAt(0);
  } else {
    clearImg();
    showIcon();
  }
}
