// Wave-2 user operations: launch variants, suggestions, broken-shortcut cleanup, uninstall / validate.
import { store } from './store.js';
import { toast } from './components/toast.js';
import { confirm } from './components/modal.js';
import { plural } from './util.js';
import * as A from './actions.js';

const call = (name, args, opts) => A.run(name, args, opts);

/** Resolves true once `id` is in the library (after a `games` event), false after `ms`. */
export function whenGame(id, ms = 4000) {
  if (store.get(id)) return Promise.resolve(true);
  return new Promise((resolve) => {
    let timer = null;
    const off = store.on('change', (changed) => { if (changed.has('games') && store.get(id)) done(true); });
    function done(v) { off(); clearTimeout(timer); resolve(v); }
    timer = setTimeout(() => done(false), ms);
  });
}

/** Keeps the details page on the group's (new) primary game, which replaces the current card. */
async function followPrimary(primaryId, memberIds) {
  const r = store.state.route;
  if (r.view !== 'game' || r.id === primaryId || !memberIds.includes(r.id)) return;
  if (await whenGame(primaryId)) A.openDetails(primaryId);
}

export const openExternal = (url) => (url ? call('openExternal', { url }) : null);

// ---------------------------------------------------------------- variants
export async function variantGroupOf(id) {
  const data = await call('variantGroups');
  return (data?.groups || []).find((g) => (g.memberIds || []).includes(id)) || null;
}

export async function groupVariants(ids, primaryId) {
  const unique = [...new Set(ids)];
  if (unique.length < 2) { toast('Escolha pelo menos dois jogos para agrupar.', { kind: 'err' }); return null; }
  const data = await call('groupVariants', { ids: unique, primaryId });
  if (!data) return null;
  toast(data.message || 'Jogos agrupados.');
  refreshSuggestions();
  followPrimary(primaryId, unique);
  return data;
}

export async function ungroupVariants(groupId) {
  const data = await call('ungroupVariants', { groupId });
  if (data) { toast(data.message || 'Variações desagrupadas.'); refreshSuggestions(); }
  return data;
}

export async function setVariantPrimary(groupId, primaryId, memberIds = []) {
  const data = await call('setVariantPrimary', { groupId, primaryId });
  if (data) { toast(data.message || 'Versão principal alterada.'); followPrimary(primaryId, memberIds); }
  return data;
}

export async function setVariantLabel(id, label) {
  const data = await call('setVariantLabel', { id, label });
  if (data) toast(data.message || 'Nome da variação salvo.');
  return data;
}

export async function dismissVariants(ids) {
  const data = await call('dismissVariants', { ids });
  if (data) {
    const key = [...ids].sort().join('|');
    store.set({ suggestions: store.state.suggestions.filter((s) => [...s.memberIds].sort().join('|') !== key) });
  }
  return data;
}

let sugSeq = 0;
/** Background refresh of "looks like the same game" suggestions (no toast on failure). */
export async function refreshSuggestions() {
  const bridge = A.getBridge();
  if (!bridge) return;
  const seq = ++sugSeq;
  try {
    const data = await bridge.call('variantSuggestions');
    if (seq === sugSeq) store.set({ suggestions: Array.isArray(data.groups) ? data.groups : [] });
  } catch (err) {
    bridge.log('warn', `variantSuggestions failed: ${err.message}`);
  }
}

// ---------------------------------------------------------------- broken shortcuts
async function undoAll(tokens) {
  let n = 0;
  for (const token of tokens) {
    try { await A.getBridge().call('undo', { token }); n++; }
    catch (err) { toast(err.message || 'Não foi possível desfazer.', { kind: 'err' }); }
  }
  if (n) toast(`${plural(n, 'atalho restaurado', 'atalhos restaurados')}.`);
}

export async function cleanupBroken() {
  const data = await call('cleanupBroken');
  if (!data) return null;
  const results = data.results || [];
  const done = results.filter((r) => r.ok);
  const failed = results.filter((r) => !r.ok);
  const removed = new Set(done.map((r) => r.gameId).filter(Boolean));
  if (removed.size) store.set({ games: store.state.games.filter((g) => !removed.has(g.id)) });
  const tokens = done.map((r) => r.undoToken).filter(Boolean);
  let text = done.length
    ? `${plural(done.length, 'atalho quebrado removido', 'atalhos quebrados removidos')} da biblioteca.`
    : 'Nenhum atalho foi removido.';
  if (failed.length) text += ` ${plural(failed.length, 'não pôde ser removido', 'não puderam ser removidos')}: ${failed[0].message || ''}`;
  toast(text, {
    kind: done.length ? 'ok' : 'err',
    action: tokens.length ? { label: tokens.length > 1 ? 'Desfazer tudo' : 'Desfazer', run: () => undoAll(tokens) } : null,
  });
  return results;
}

// ---------------------------------------------------------------- uninstall / validate
/** Confirmation text per uninstall method; null when there is no uninstaller ("none"). */
export function uninstallText(g, un) {
  const name = g?.name || 'este jogo';
  switch (un?.method) {
    case 'steam': return { message: `A Steam vai abrir a desinstalação de ${name}.`, label: 'Abrir a Steam', danger: false };
    case 'epic': return { message: `Abra a biblioteca da Epic para desinstalar ${name}. O GamesHub vai abrir a Epic Games Launcher.`, label: 'Abrir a Epic', danger: false };
    case 'registry': return {
      message: `O desinstalador de ${un.displayName || name} será aberto. Siga as instruções dele para concluir. O jogo sai da biblioteca na próxima verificação.`,
      label: 'Abrir desinstalador', danger: true,
    };
    default: return null;
  }
}

export async function uninstall(id, un) {
  const g = store.get(id);
  const text = uninstallText(g, un);
  if (!g || !text) return null;
  const yes = await confirm({ title: `Desinstalar ${g.name}`, message: text.message, confirmLabel: text.label, danger: text.danger });
  if (!yes) return null;
  const data = await call('uninstallGame', { id });
  if (data) toast(data.message || 'Desinstalação iniciada.', { kind: 'info' });
  return data;
}

export async function validate(id) {
  const data = await call('validateGame', { id });
  if (data) toast(data.message || 'A Steam vai verificar os arquivos.', { kind: 'info' });
  return data;
}
