// App state + pure derived selectors (filter / search / sort). DOM-free so it can be tested in Node.
import { Emitter } from './emitter.js';
import { normalize } from './util.js';

export const DEFAULT_SETTINGS = {
  gamesDir: '', importSteam: true, importEpic: true, onLaunch: 'tray', startWithWindows: false,
  startMinimized: false, closeToTray: true, hotkeyEnabled: true, hotkey: 'Ctrl+Alt+G', sortBy: 'name',
  view: 'grid', cardStyle: 'landscape', reduceMotion: false, autoArtwork: true, steamGridDbKey: '',
  trackPlaytime: true, checkUpdates: true, updateRepo: '', language: 'pt-BR',
  // integrations
  importRiot: true, importHydra: true, importSteamPlaytime: true, fetchMetadata: true, pcgwEnabled: true,
  quickLaunchEnabled: true, quickLaunchHotkey: 'Ctrl+Shift+Space', automationEnabled: true,
};

export const LIBRARY_SECTIONS = [
  { key: 'all', label: 'Todos' },
  { key: 'favorites', label: 'Favoritos' },
  { key: 'recent', label: 'Jogados recentemente' },
  { key: 'hidden', label: 'Ocultos' },
];

export const SORTS = [
  { key: 'name', label: 'Nome' },
  { key: 'recent', label: 'Jogados recentemente' },
  { key: 'playtime', label: 'Mais jogados' },
  { key: 'added', label: 'Adicionados recentemente' },
  { key: 'size', label: 'Maior tamanho' },
];

/** Sorts the backend persists (settings.sortBy enum); others are client-side only (localStorage). */
export const SERVER_SORTS = ['name', 'recent', 'playtime', 'added'];

export const QUICK_FILTERS = [
  { key: 'never', label: 'Nunca jogados' },
  { key: 'stale', label: 'Sem jogar há 3+ meses' },
  { key: 'installed', label: 'Instalados' },
];
const STALE_MS = 90 * 86400000;

// Section keys: 'all' | 'favorites' | 'recent' | 'hidden' | 'platform:<name>' | 'collection:<name>'
export function sectionLabel(key) {
  const fixed = LIBRARY_SECTIONS.find((s) => s.key === key);
  if (fixed) return fixed.label;
  const i = key.indexOf(':');
  return i > 0 ? key.slice(i + 1) : key;
}

const size = (g) => (Number(g.sizeBytes) > 0 ? Number(g.sizeBytes) : -1);
const time = (iso) => { const t = iso ? Date.parse(iso) : NaN; return Number.isNaN(t) ? 0 : t; };

export function filterSection(games, key) {
  if (key === 'hidden') return games.filter((g) => g.hidden);
  const visible = games.filter((g) => !g.hidden);
  if (key === 'favorites') return visible.filter((g) => g.favorite);
  if (key === 'recent') return visible.filter((g) => g.lastPlayed);
  if (key.startsWith('platform:')) { const p = key.slice(9); return visible.filter((g) => g.platform === p); }
  if (key.startsWith('collection:')) {
    const c = key.slice(11);
    return visible.filter((g) => (g.collections || []).includes(c));
  }
  return visible;
}

export function queryTokens(query) {
  return normalize(query).split(/\s+/).filter(Boolean);
}

/** Every token must appear in name, platform, a collection or a genre (accent/case-insensitive). */
export function matchesQuery(game, tokens) {
  if (!tokens.length) return true;
  const hay = normalize([game.name, game.platform, ...(game.collections || []), ...(game.genres || [])].join(' '));
  return tokens.every((t) => hay.includes(t));
}

export function searchGames(games, query) {
  const tokens = queryTokens(query);
  return tokens.length ? games.filter((g) => matchesQuery(g, tokens)) : games;
}

const collator = typeof Intl !== 'undefined'
  ? new Intl.Collator('pt-BR', { sensitivity: 'base', numeric: true }) : null;
export const byName = (a, b) => (collator ? collator.compare(a.name, b.name) : a.name.localeCompare(b.name));

export function sortGames(games, sortBy) {
  const list = games.slice();
  switch (sortBy) {
    case 'recent': return list.sort((a, b) => time(b.lastPlayed) - time(a.lastPlayed) || byName(a, b));
    case 'playtime': return list.sort((a, b) => (b.playSeconds || 0) - (a.playSeconds || 0) || byName(a, b));
    case 'added': return list.sort((a, b) => time(b.addedAt) - time(a.addedAt) || byName(a, b));
    case 'size': return list.sort((a, b) => size(b) - size(a) || byName(a, b)); // unknown (-1/0) last
    default: return list.sort(byName);
  }
}

/** Genre filter ('' = all). `genres` is an optional GameDto field. */
export function filterGenre(games, genre) {
  return genre ? games.filter((g) => (g.genres || []).includes(genre)) : games;
}

/** Sorted, de-duplicated genres present in non-hidden games. */
export function genreList(games) {
  const set = new Set();
  for (const g of games) if (!g.hidden) for (const x of g.genres || []) if (x) set.add(x);
  return [...set].sort((a, b) => (collator ? collator.compare(a, b) : a.localeCompare(b)));
}

/** Quick filter ('' = none): never played, not played for 3+ months, installed (not broken). */
export function filterQuick(games, key, now = Date.now()) {
  switch (key) {
    case 'never': return games.filter((g) => !g.lastPlayed && !(g.playSeconds > 0));
    case 'stale': return games.filter((g) => g.lastPlayed && !g.running && now - time(g.lastPlayed) >= STALE_MS);
    case 'installed': return games.filter((g) => !g.broken);
    default: return games;
  }
}

/** Games shown in the library grid for the current section/genre/quick filter/query/sort. */
export function selectVisible(games, { section = 'all', query = '', sortBy = 'name', genre = '', quick = '', now } = {}) {
  const inSection = filterQuick(filterGenre(filterSection(games, section), genre), quick, now);
  const found = searchGames(inSection, query);
  return sortGames(found, section === 'recent' ? 'recent' : sortBy);
}

export function recentGames(games, n = 10) {
  return sortGames(games.filter((g) => !g.hidden && g.lastPlayed), 'recent').slice(0, n);
}

export function platformCounts(games) {
  const map = new Map();
  for (const g of games) if (!g.hidden) map.set(g.platform, (map.get(g.platform) || 0) + 1);
  return [...map].map(([platform, count]) => ({ platform, count }))
    .sort((a, b) => b.count - a.count || a.platform.localeCompare(b.platform));
}

export function collectionCounts(games, collections = []) {
  const names = [...collections];
  for (const g of games) for (const c of g.collections || []) if (!names.includes(c)) names.push(c);
  return names.map((name) => ({
    name, count: games.filter((g) => !g.hidden && (g.collections || []).includes(name)).length,
  }));
}

export function sectionCounts(games) {
  const visible = games.filter((g) => !g.hidden);
  return {
    all: visible.length,
    favorites: visible.filter((g) => g.favorite).length,
    recent: visible.filter((g) => g.lastPlayed).length,
    hidden: games.length - visible.length,
  };
}

/** Hero game: running > most recently played > favorite with art > any game with art > first. */
export function pickFeatured(games) {
  const visible = games.filter((g) => !g.hidden);
  if (!visible.length) return null;
  const running = visible.find((g) => g.running);
  if (running) return running;
  const [recent] = recentGames(visible, 1);
  if (recent) return recent;
  const hasArt = (g) => g.art && (g.art.hero || g.art.header);
  return visible.find((g) => g.favorite && hasArt(g)) || visible.find(hasArt) || sortGames(visible, 'name')[0];
}

/** clientSort ('size') overrides the persisted settings.sortBy. */
export const effectiveSort = (s) => s.clientSort || s.settings.sortBy;

export function createStore(initial = {}) {
  const em = new Emitter();
  const state = {
    status: 'loading', error: null, demo: false, version: '',
    games: [], byId: new Map(), collections: [],
    settings: { ...DEFAULT_SETTINGS },
    windowState: { maximized: false, fullscreen: false, pinned: false },
    route: { view: 'library', id: null },
    section: 'all', query: '', genre: '', quick: '', clientSort: '',
    suggestions: [], // extras: variantSuggestions groups
    bigPicture: false, sidebarCollapsed: false,
    update: null, updatePercent: null, padActive: false,
    ...initial,
  };

  function set(patch) {
    const changed = new Set();
    for (const [k, v] of Object.entries(patch)) {
      if (state[k] === v) continue;
      state[k] = v;
      changed.add(k);
    }
    if (changed.has('games')) state.byId = new Map(state.games.map((g) => [g.id, g]));
    if (changed.size) em.emit('change', changed);
    return changed;
  }

  /** Optimistic local patch of one game (the next `games` event from C# is authoritative). */
  function patchGame(id, patch) {
    const idx = state.games.findIndex((g) => g.id === id);
    if (idx < 0) return;
    const games = state.games.slice();
    games[idx] = { ...games[idx], ...patch };
    set({ games });
  }

  return {
    state, set, patchGame,
    on: (n, f) => em.on(n, f),
    get: (id) => state.byId.get(id),
    visible: () => selectVisible(state.games, {
      section: state.section, query: state.query, sortBy: effectiveSort(state), genre: state.genre, quick: state.quick,
    }),
  };
}

export const store = createStore();
