// Markup helpers for settings rows. `data-set` controls are saved/rendered generically by settings.js.
import { icon } from '../components/icons.js';
import { esc } from '../util.js';

const text = (title, desc) => `<span class="set-text"><b>${title}</b>${desc ? `<small>${desc}</small>` : ''}</span>`;

export const sw = (key, title, desc = '') =>
  `<label class="set-row">${text(title, desc)}<input type="checkbox" class="switch" role="switch" data-nav data-set="${key}"></label>`;

export const sel = (key, title, desc, options) =>
  `<label class="set-row">${text(title, desc)}` +
  `<span class="select-wrap"><select class="select" data-nav data-set="${key}">${options.map(([v, l]) => `<option value="${v}">${esc(l)}</option>`).join('')}</select>${icon('chevronDown')}</span></label>`;

export const row = (title, desc, control) =>
  `<div class="set-row">${text(title, desc)}<span class="set-control">${control}</span></div>`;

export const group = (id, title, content) =>
  `<section class="set-group" aria-labelledby="sg-${id}"><h2 class="set-title" id="sg-${id}">${title}</h2><div class="set-card">${content}</div></section>`;
