import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb, game, daysAgo } from './_setup.mjs';

const S = await importWeb('js/store.js');
const NOW = Date.parse('2025-06-15T12:00:00.000Z');
const names = (list) => list.map((g) => g.name);

const LIB = [
  game({ id: 'steam:730', name: 'Counter-Strike 2', platform: 'Steam', favorite: true, collections: ['Competitivo'], genres: ['Tiro'], lastPlayed: daysAgo(1, NOW), playSeconds: 5000, addedAt: daysAgo(300, NOW), sizeBytes: 30e9, art: { header: 'h', hero: null } }),
  game({ id: 'folder:pokemon.lnk', name: 'Pokémon Edição Ouro', platform: 'PC', genres: ['RPG'], addedAt: daysAgo(10, NOW) }),
  game({ id: 'epic:fn', name: 'Fortnite', platform: 'Epic', collections: ['Com amigos'], lastPlayed: daysAgo(120, NOW), playSeconds: 9000, addedAt: daysAgo(200, NOW), sizeBytes: 20e9 }),
  game({ id: 'folder:hidden.lnk', name: 'Oculto', hidden: true, favorite: true, lastPlayed: daysAgo(0, NOW), collections: ['Competitivo'], genres: ['Terror'] }),
  game({ id: 'folder:broken.lnk', name: 'Ábaco Quebrado', broken: true, lastPlayed: daysAgo(95, NOW), playSeconds: 60, addedAt: daysAgo(1, NOW), sizeBytes: 0 }),
  game({ id: 'folder:jogo10.lnk', name: 'Jogo 10', sizeBytes: 5e9, addedAt: daysAgo(5, NOW) }),
  game({ id: 'folder:jogo2.lnk', name: 'Jogo 2', sizeBytes: 5e9, playSeconds: 30, addedAt: daysAgo(5, NOW) }),
];

describe('filterSection', () => {
  test('all excludes hidden games', () => {
    assert.deepEqual(names(S.filterSection(LIB, 'all')).includes('Oculto'), false);
    assert.equal(S.filterSection(LIB, 'all').length, LIB.length - 1);
  });
  test('favorites / recent / hidden', () => {
    assert.deepEqual(names(S.filterSection(LIB, 'favorites')), ['Counter-Strike 2']);
    assert.deepEqual(names(S.filterSection(LIB, 'recent')).sort(), ['Counter-Strike 2', 'Fortnite', 'Ábaco Quebrado'].sort());
    assert.deepEqual(names(S.filterSection(LIB, 'hidden')), ['Oculto']);
  });
  test('platform:<name> and collection:<name>', () => {
    assert.deepEqual(names(S.filterSection(LIB, 'platform:Epic')), ['Fortnite']);
    assert.deepEqual(names(S.filterSection(LIB, 'platform:Nada')), []);
    assert.deepEqual(names(S.filterSection(LIB, 'collection:Competitivo')), ['Counter-Strike 2']); // hidden one excluded
    assert.deepEqual(names(S.filterSection(LIB, 'collection:Com amigos')), ['Fortnite']);
  });
  test('sectionLabel', () => {
    assert.equal(S.sectionLabel('favorites'), 'Favoritos');
    assert.equal(S.sectionLabel('platform:Steam'), 'Steam');
    assert.equal(S.sectionLabel('collection:Com amigos'), 'Com amigos');
    assert.equal(S.sectionLabel('weird'), 'weird');
  });
});

describe('search', () => {
  const q = (query) => names(S.searchGames(LIB, query));
  test('accent- and case-insensitive both ways', () => {
    assert.deepEqual(q('pokemon'), ['Pokémon Edição Ouro']);
    assert.deepEqual(q('POKÉMON edicao'), ['Pokémon Edição Ouro']);
    assert.deepEqual(q('abaco'), ['Ábaco Quebrado']);
  });
  test('every word must match (AND), in any order', () => {
    assert.deepEqual(q('strike counter'), ['Counter-Strike 2']);
    assert.deepEqual(q('counter fortnite'), []);
    assert.deepEqual(q('  jogo   10 '), ['Jogo 10']);
  });
  test('matches platform, collection and genre', () => {
    assert.deepEqual(q('epic'), ['Fortnite']);
    assert.deepEqual(q('amigos'), ['Fortnite']);
    assert.deepEqual(q('rpg'), ['Pokémon Edição Ouro']);
    assert.deepEqual(q('tiro steam'), ['Counter-Strike 2']);
  });
  test('empty/blank query returns the input untouched', () => {
    assert.equal(S.searchGames(LIB, ''), LIB);
    assert.equal(S.searchGames(LIB, '   '), LIB);
    assert.deepEqual(S.queryTokens(' Á  b '), ['a', 'b']);
  });
  test('tolerates games without collections/genres', () => {
    const bare = { ...game({ name: 'Solo' }), collections: undefined, genres: undefined };
    assert.equal(S.matchesQuery(bare, ['solo']), true);
  });
});

describe('sortGames', () => {
  const sorted = (key, list = S.filterSection(LIB, 'all')) => names(S.sortGames(list, key));
  test('name: pt-BR collation, numeric, accents ignored', () => {
    assert.deepEqual(sorted('name'), ['Ábaco Quebrado', 'Counter-Strike 2', 'Fortnite', 'Jogo 2', 'Jogo 10', 'Pokémon Edição Ouro']);
  });
  test('recent: newest first, never-played last (by name)', () => {
    assert.deepEqual(sorted('recent'), ['Counter-Strike 2', 'Ábaco Quebrado', 'Fortnite', 'Jogo 2', 'Jogo 10', 'Pokémon Edição Ouro']);
  });
  test('playtime: most played first, ties by name', () => {
    assert.deepEqual(sorted('playtime').slice(0, 4), ['Fortnite', 'Counter-Strike 2', 'Ábaco Quebrado', 'Jogo 2']);
  });
  test('added: newest first, ties by name', () => {
    assert.deepEqual(sorted('added').slice(0, 3), ['Ábaco Quebrado', 'Jogo 2', 'Jogo 10']);
  });
  test('size (client-side): biggest first, unknown/zero last', () => {
    assert.deepEqual(sorted('size'), ['Counter-Strike 2', 'Fortnite', 'Jogo 2', 'Jogo 10', 'Ábaco Quebrado', 'Pokémon Edição Ouro']);
  });
  test('unknown key falls back to name; input is not mutated', () => {
    const input = S.filterSection(LIB, 'all');
    const before = names(input);
    assert.deepEqual(sorted('bogus'), sorted('name'));
    S.sortGames(input, 'size');
    assert.deepEqual(names(input), before);
  });
  test('invalid dates sort as oldest', () => {
    const list = [game({ name: 'A', lastPlayed: 'not a date' }), game({ name: 'B', lastPlayed: daysAgo(3, NOW) })];
    assert.deepEqual(names(S.sortGames(list, 'recent')), ['B', 'A']);
  });
});

describe('genre and quick filters', () => {
  test('genreList: sorted, unique, hidden games ignored', () => {
    const list = [...LIB, game({ name: 'X', genres: ['RPG', 'Ação', ''] })];
    assert.deepEqual(S.genreList(list), ['Ação', 'RPG', 'Tiro']);
  });
  test('filterGenre', () => {
    assert.deepEqual(names(S.filterGenre(LIB, 'RPG')), ['Pokémon Edição Ouro']);
    assert.equal(S.filterGenre(LIB, ''), LIB);
  });
  test('never played: no lastPlayed and no play time', () => {
    const r = names(S.filterQuick(S.filterSection(LIB, 'all'), 'never', NOW));
    assert.deepEqual(r.sort(), ['Jogo 10', 'Pokémon Edição Ouro'].sort()); // Jogo 2 has 30 s of play time
  });
  test('stale: played, 90+ days ago, not running', () => {
    const list = [...LIB,
      game({ name: 'Exato 90', lastPlayed: daysAgo(90, NOW) }),
      game({ name: 'Quase', lastPlayed: daysAgo(89.9, NOW) }),
      game({ name: 'Rodando', lastPlayed: daysAgo(200, NOW), running: true })];
    assert.deepEqual(names(S.filterQuick(list, 'stale', NOW)).sort(), ['Exato 90', 'Fortnite', 'Ábaco Quebrado'].sort());
  });
  test('installed excludes broken shortcuts; unknown key is a no-op', () => {
    assert.equal(S.filterQuick(LIB, 'installed', NOW).some((g) => g.broken), false);
    assert.equal(S.filterQuick(LIB, '', NOW), LIB);
    assert.deepEqual(S.QUICK_FILTERS.map((f) => f.key), ['never', 'stale', 'installed']);
  });
  test('selectVisible composes section + genre + quick + query + sort', () => {
    assert.deepEqual(names(S.selectVisible(LIB, { section: 'all', query: 'jogo', sortBy: 'name' })), ['Jogo 2', 'Jogo 10']);
    assert.deepEqual(names(S.selectVisible(LIB, { section: 'all', quick: 'installed', sortBy: 'size', now: NOW })).at(-1), 'Pokémon Edição Ouro');
    assert.deepEqual(names(S.selectVisible(LIB, { genre: 'Tiro' })), ['Counter-Strike 2']);
    // the "recent" section always sorts by recency, whatever sortBy says
    assert.deepEqual(names(S.selectVisible(LIB, { section: 'recent', sortBy: 'name' })), ['Counter-Strike 2', 'Ábaco Quebrado', 'Fortnite']);
  });
});

describe('counts and featured', () => {
  test('sectionCounts', () => {
    assert.deepEqual(S.sectionCounts(LIB), { all: 6, favorites: 1, recent: 3, hidden: 1 });
  });
  test('platformCounts: by count desc then name, hidden ignored', () => {
    assert.deepEqual(S.platformCounts(LIB), [{ platform: 'PC', count: 4 }, { platform: 'Epic', count: 1 }, { platform: 'Steam', count: 1 }]);
  });
  test('collectionCounts: declared (even empty) first, then discovered; hidden ignored', () => {
    assert.deepEqual(S.collectionCounts(LIB, ['Vazia', 'Competitivo']),
      [{ name: 'Vazia', count: 0 }, { name: 'Competitivo', count: 1 }, { name: 'Com amigos', count: 1 }]);
  });
  test('recentGames', () => {
    assert.deepEqual(names(S.recentGames(LIB, 2)), ['Counter-Strike 2', 'Ábaco Quebrado']);
  });
  test('pickFeatured priority: running > recent > favorite with art > any art > first by name', () => {
    assert.equal(S.pickFeatured([]), null);
    assert.equal(S.pickFeatured([game({ name: 'H', hidden: true })]), null);
    const a = game({ name: 'Zeta' });
    const b = game({ name: 'Alfa' });
    assert.equal(S.pickFeatured([a, b]).name, 'Alfa');
    const art = game({ name: 'ComArte', art: { hero: 'x' } });
    assert.equal(S.pickFeatured([a, b, art]).name, 'ComArte');
    const fav = game({ name: 'Fav', favorite: true, art: { header: 'y' } });
    assert.equal(S.pickFeatured([a, art, fav]).name, 'Fav');
    const recent = game({ name: 'Recente', lastPlayed: daysAgo(3, NOW) });
    assert.equal(S.pickFeatured([a, art, fav, recent]).name, 'Recente');
    const running = game({ name: 'Rodando', running: true });
    assert.equal(S.pickFeatured([a, art, fav, recent, running]).name, 'Rodando');
    assert.equal(S.pickFeatured([game({ name: 'Oculto', hidden: true, running: true }), a]).name, 'Zeta');
  });
});

describe('createStore', () => {
  test('set() emits one change event with the changed keys only', () => {
    const st = S.createStore();
    const seen = [];
    st.on('change', (keys) => seen.push([...keys].sort()));
    assert.deepEqual([...st.set({ query: 'a', section: 'all' })], ['query']); // section unchanged
    st.set({ query: 'a' });                                                   // no-op: no event
    assert.deepEqual(seen, [['query']]);
  });
  test('games -> byId index; get(); patchGame is immutable and emits', () => {
    const st = S.createStore();
    const g = game({ id: 'x', name: 'X' });
    st.set({ games: [g] });
    assert.equal(st.get('x'), g);
    let events = 0;
    st.on('change', (k) => { if (k.has('games')) events++; });
    st.patchGame('x', { favorite: true });
    assert.equal(st.get('x').favorite, true);
    assert.equal(g.favorite, false, 'original object untouched');
    st.patchGame('missing', { favorite: true });
    assert.equal(events, 1);
  });
  test('visible() uses clientSort ("size") over settings.sortBy', () => {
    const st = S.createStore({ games: LIB });
    st.set({ games: LIB.slice() });
    assert.equal(st.visible()[0].name, 'Ábaco Quebrado');
    st.set({ clientSort: 'size' });
    assert.equal(st.visible()[0].name, 'Counter-Strike 2');
    assert.equal(S.effectiveSort(st.state), 'size');
    st.set({ clientSort: '', settings: { ...st.state.settings, sortBy: 'playtime' } });
    assert.equal(st.visible()[0].name, 'Fortnite');
  });
  test('initial state uses DEFAULT_SETTINGS and the library route', () => {
    const st = S.createStore();
    assert.deepEqual(st.state.settings, S.DEFAULT_SETTINGS);
    assert.notEqual(st.state.settings, S.DEFAULT_SETTINGS, 'copied, not shared');
    assert.deepEqual(st.state.route, { view: 'library', id: null });
  });
  test('sort lists: every server sort is offered; size is client-only', () => {
    const keys = S.SORTS.map((s) => s.key);
    for (const k of S.SERVER_SORTS) assert.ok(keys.includes(k), k);
    assert.ok(keys.includes('size') && !S.SERVER_SORTS.includes('size'));
  });
});
