// User-level operations: call the bridge, update the store optimistically, report via toasts.
import { store } from './store.js';
import { toast } from './components/toast.js';
import { confirm, prompt } from './components/modal.js';
import { openMenu } from './components/contextmenu.js';

let bridge = null;
export const setBridge = (b) => { bridge = b; };
export const getBridge = () => bridge;

const report = (data, fallback) => {
  const text = data?.message || fallback;
  if (text) toast(text, { kind: 'ok', undoToken: data?.undoToken || null });
};
const fail = (err) => toast(err?.message || 'Algo deu errado.', { kind: 'err' });

/** Generic command wrapper: resolves data or null (errors are toasted). */
export async function run(name, args, opts) {
  try { return await bridge.call(name, args, opts); }
  catch (err) { fail(err); return null; }
}

// ---------------------------------------------------------------- navigation
export function goLibrary(section) {
  const patch = { route: { view: 'library', id: null } };
  if (section) patch.section = section;
  store.set(patch);
}
export const openDetails = (id) => { if (store.get(id)) store.set({ route: { view: 'game', id } }); };
export const openSettings = () => store.set({ route: { view: 'settings', id: null } });
export function back() {
  const { route, bigPicture, query } = store.state;
  if (bigPicture) { toggleBigPicture(false); return true; }
  if (route.view !== 'library') { goLibrary(); return true; }
  if (query) { store.set({ query: '' }); return true; }
  return false;
}

export function focusSearch() {
  if (store.state.bigPicture) toggleBigPicture(false);
  if (store.state.route.view !== 'library') goLibrary();
  requestAnimationFrame(() => {
    const input = document.getElementById('search');
    input?.focus();
    input?.select();
  });
}

export async function toggleBigPicture(on = !store.state.bigPicture) {
  store.set({ bigPicture: on });
  const ws = await run('window', { action: 'fullscreen', on });
  if (ws) store.set({ windowState: ws });
}

export async function windowAction(action, on) {
  const ws = await run('window', on === undefined ? { action } : { action, on });
  if (ws) store.set({ windowState: ws });
}

// ---------------------------------------------------------------- games
export async function launch(id, variantId) {
  const g = store.get(id);
  if (!g) return;
  if (g.running) { toast(`${g.name} já está em execução.`, { kind: 'info' }); return; }
  const data = await run('launch', variantId ? { id, variantId } : { id });
  if (data) report(data, `Iniciando ${g.name}…`);
}

/** Launch variants (optional field): [{ id, label }]. */
export const variantsOf = (g) => (Array.isArray(g?.variants) ? g.variants.filter((v) => v && v.id) : []);

export function variantMenuItems(id) {
  return variantsOf(store.get(id)).map((v) => ({ label: v.label || v.id, icon: 'play', action: () => launch(id, v.id) }));
}

export function openVariantMenu(id, anchor) {
  const r = anchor.getBoundingClientRect();
  openMenu(variantMenuItems(id), { x: r.left, y: r.bottom + 4 });
}

export async function updateGame(id, edit, { silent = false } = {}) {
  const g = store.get(id);
  if (!g) return null;
  const optimistic = {};
  for (const k of ['favorite', 'hidden', 'collections', 'launchArgs', 'steamAppId']) if (k in edit) optimistic[k] = edit[k];
  if (edit.name) optimistic.name = edit.name;
  const before = { ...g };
  store.patchGame(id, optimistic);
  try {
    const data = await bridge.call('updateGame', { id, edit });
    if (!silent) report(data);
    return data;
  } catch (err) {
    store.patchGame(id, before);
    fail(err);
    return null;
  }
}

export const toggleFavorite = (id) => {
  const g = store.get(id);
  if (g) updateGame(id, { favorite: !g.favorite });
};
export const setHidden = (id, hidden) => updateGame(id, { hidden });

export function toggleCollection(id, name) {
  const g = store.get(id);
  if (!g) return;
  const cols = g.collections || [];
  const next = cols.includes(name) ? cols.filter((c) => c !== name) : [...cols, name];
  return updateGame(id, { collections: next }, { silent: true }).then((d) => {
    if (d) toast(cols.includes(name) ? `Removido de "${name}".` : `Adicionado a "${name}".`);
  });
}

export async function newCollectionFor(id) {
  const name = await prompt({ title: 'Nova coleção', label: 'Nome da coleção', placeholder: 'Ex.: Com amigos', confirmLabel: 'Criar' });
  if (!name) return;
  const g = store.get(id);
  if (g && !(g.collections || []).includes(name)) await toggleCollection(id, name);
  if (!store.state.collections.includes(name)) store.set({ collections: [...store.state.collections, name] });
}

export async function removeGame(id) {
  const g = store.get(id);
  if (!g) return;
  const message = g.source === 'folder'
    ? `O atalho de "${g.name}" será movido para a lixeira do GamesHub. Os arquivos do jogo não são apagados.`
    : `"${g.name}" deixará de aparecer na biblioteca. O jogo não será desinstalado.`;
  const yes = await confirm({ title: 'Remover jogo', message, confirmLabel: 'Remover', danger: true });
  if (!yes) return;
  const data = await run('remove', { id });
  if (!data) return;
  if (store.state.route.view === 'game' && store.state.route.id === id) goLibrary();
  store.set({ games: store.state.games.filter((x) => x.id !== id) });
  report(data, `${g.name} removido.`);
}

export async function undo(token) {
  const data = await run('undo', { token });
  if (data) report({ message: data.message || 'Ação desfeita.' });
}

export async function reveal(id) {
  const data = await run('reveal', { id });
  if (data && data.message) toast(data.message, { kind: 'info' });
}

export async function rescan() {
  toast('Procurando jogos…', { kind: 'info', duration: 2000 });
  const data = await run('rescan');
  if (data) report(data, 'Biblioteca atualizada.');
}

// ---------------------------------------------------------------- add
/** Sends dropped File objects to C# (they carry the real path in WebView2). */
export async function addFiles(files) {
  const list = Array.from(files || []);
  if (!list.length) return [];
  const data = await run('addFiles', {}, { files: list });
  const results = data?.results || [];
  for (const r of results) toast(r.message || (r.ok ? 'Jogo adicionado.' : 'Não foi possível adicionar.'), { kind: r.ok ? 'ok' : 'err', undoToken: r.undoToken });
  return results;
}

export async function pickFile() {
  const data = await run('pickFile');
  if (!data || data.cancelled) return null;
  report(data, 'Jogo adicionado.');
  return data;
}

export async function addSteam(appId, name) {
  const data = await run('addSteam', { appId: String(appId), name });
  if (data) report(data, `${name || 'Jogo'} adicionado.`);
  return data;
}

// ---------------------------------------------------------------- settings
/** Optimistic; reverts the patched keys when the backend rejects them. opts.onError(message) replaces the toast. */
export async function saveSettings(patch, { onError = null } = {}) {
  const before = {};
  for (const k of Object.keys(patch)) before[k] = store.state.settings[k];
  store.set({ settings: { ...store.state.settings, ...patch } });
  try {
    const s = await bridge.call('setSettings', { patch });
    store.set({ settings: { ...store.state.settings, ...s } });
    return true;
  } catch (err) {
    store.set({ settings: { ...store.state.settings, ...before } });
    if (onError) onError(err?.message || 'Algo deu errado.'); else fail(err);
    return false;
  }
}

// ---------------------------------------------------------------- context menu
export function gameMenuItems(id) {
  const g = store.get(id);
  if (!g) return [];
  return [
    { label: g.running ? 'Em execução' : 'Jogar', icon: 'play', hint: 'Enter', disabled: g.running, action: () => launch(id) },
    ...(variantsOf(g).length ? [{ label: 'Jogar como', icon: 'play', submenu: () => variantMenuItems(id) }] : []),
    { label: 'Detalhes', icon: 'info', hint: 'I', action: () => openDetails(id) },
    { label: g.favorite ? 'Remover dos favoritos' : 'Favoritar', icon: g.favorite ? 'starFill' : 'star', hint: 'F', action: () => toggleFavorite(id) },
    {
      label: 'Adicionar à coleção', icon: 'tag',
      submenu: () => {
        const cur = store.get(id)?.collections || [];
        const names = [...new Set([...store.state.collections, ...cur])];
        return [
          ...names.map((n) => ({ label: n, checked: cur.includes(n), action: () => toggleCollection(id, n) })),
          ...(names.length ? [{ separator: true }] : []),
          { label: 'Nova coleção…', icon: 'plus', action: () => newCollectionFor(id) },
        ];
      },
    },
    { separator: true },
    { label: 'Abrir local', icon: 'folder', action: () => reveal(id) },
    { label: g.hidden ? 'Mostrar na biblioteca' : 'Ocultar', icon: g.hidden ? 'eye' : 'eyeOff', action: () => setHidden(id, !g.hidden) },
    { label: 'Remover…', icon: 'trash', hint: 'Del', danger: true, action: () => removeGame(id) },
  ];
}

export function openGameMenu(id, at) {
  openMenu(gameMenuItems(id), at);
}
