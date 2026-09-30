// Pure helpers of "Encontrar jogos no PC" (views/discover.js): grouping, labels, markup and the addDiscovered
// payload. DOM-free so they can be unit-tested in Node.
import { esc, fmtBytes } from '../util.js';

/** Candidates at or above this confidence start checked (GameEvidence.Preselect in C#). */
export const CONFIDENT = 0.8;
export const isPreselected = (c) => Number(c?.confidence) >= CONFIDENT;

export const SOURCE_LABELS = { registry: 'Programas instalados', folder: 'Pasta no disco', xbox: 'Xbox / Microsoft Store' };
export const sourceLabel = (s) => SOURCE_LABELS[s] || SOURCE_LABELS.folder;

/** 0.873 -> "87%" (clamped to 0..100). */
export const fmtConfidence = (c) => `${Math.round(Math.max(0, Math.min(1, Number(c?.confidence) || 0)) * 100)}%`;

/** "Unity · Steamworks · Pasta de jogos +2" (at most `max` reasons). */
export function reasonsLabel(reasons, max = 3) {
  const list = (reasons || []).filter(Boolean);
  const shown = list.slice(0, max).join(' · ');
  return list.length > max ? `${shown} +${list.length - max}` : shown;
}

/** Second line of a candidate: reasons, size (when known) and where it was found. */
export function metaLine(c) {
  const parts = [reasonsLabel(c.reasons)];
  if (Number(c.sizeBytes) > 0) parts.push(fmtBytes(c.sizeBytes));
  parts.push(sourceLabel(c.source));
  return parts.filter(Boolean).join(' — ');
}

/** [{ key, title, items: [{ c, i }] }]: "high" (>= 0.8) then "low"; each by confidence, then name. Empty groups dropped. */
export function groupCandidates(list) {
  const byConf = (a, b) => (Number(b.c.confidence) || 0) - (Number(a.c.confidence) || 0)
    || String(a.c.name).localeCompare(String(b.c.name), 'pt-BR');
  const all = (list || []).map((c, i) => ({ c, i }));
  return [
    { key: 'high', title: 'Muito provavelmente jogos', items: all.filter((x) => isPreselected(x.c)).sort(byConf) },
    { key: 'low', title: 'Talvez sejam jogos', items: all.filter((x) => !isPreselected(x.c)).sort(byConf) },
  ].filter((g) => g.items.length);
}

/** Indexes checked right after a discovery. */
export const initialSelection = (list) => new Set((list || []).map((c, i) => (isPreselected(c) ? i : -1)).filter((i) => i >= 0));

/** Items for addDiscovered: the selected candidates with their (edited, trimmed) names; empty names fall back. */
export function buildPayload(list, selected, names = new Map()) {
  return [...selected].sort((a, b) => a - b).filter((i) => list[i]).map((i) => {
    const edited = String(names.get(i) ?? '').trim();
    return { exe: list[i].exe, name: edited || list[i].name };
  });
}

/**
 * After addDiscovered: drops the candidates that were added (results are in payload order) and re-indexes the
 * selection and edited names. Returns { list, selected, names, added }.
 */
export function removeAdded(list, selected, names, results) {
  const picked = [...selected].sort((a, b) => a - b).filter((i) => list[i]);
  const added = new Set(picked.filter((_, k) => results?.[k]?.ok));
  const keep = list.map((c, i) => ({ c, i })).filter((x) => !added.has(x.i));
  const remap = new Map(keep.map((x, k) => [x.i, k]));
  return {
    list: keep.map((x) => x.c),
    selected: new Set([...selected].filter((i) => remap.has(i)).map((i) => remap.get(i))),
    names: new Map([...names].filter(([i]) => remap.has(i)).map(([i, v]) => [remap.get(i), v])),
    added: added.size,
  };
}

export function candidateHtml(c, i, checked) {
  const n = esc(c.name);
  return `<li class="disc-row" data-i="${i}">` +
    `<input type="checkbox" data-nav data-disc-pick="${i}" aria-label="Adicionar ${n}"${checked ? ' checked' : ''}>` +
    '<div class="disc-main">' +
    `<input class="input disc-name" data-nav data-disc-name="${i}" value="${n}" aria-label="Nome de ${n}" spellcheck="false" maxlength="120">` +
    `<small class="disc-meta" title="${esc((c.reasons || []).join(', '))}">${esc(metaLine(c))}</small>` +
    `<small class="disc-path" title="${esc(c.exe)}">${esc(c.exe)}</small>` +
    '</div>' +
    `<span class="chip disc-conf${isPreselected(c) ? ' chip-live' : ''}" title="Confiança">${fmtConfidence(c)}</span></li>`;
}

export function resultsHtml(list, selected) {
  return groupCandidates(list).map((g) =>
    `<section class="disc-group" aria-label="${esc(g.title)}"><h3 class="field-label">${esc(g.title)} (${g.items.length})</h3>` +
    `<ul class="disc-list">${g.items.map(({ c, i }) => candidateHtml(c, i, selected.has(i))).join('')}</ul></section>`).join('');
}
