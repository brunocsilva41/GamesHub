// Performance budget: filtering + searching + sorting a 300-game library must stay well under a frame
// budget (< 50 ms per full recompute, measured as the slowest of several runs after warm-up).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { performance } from 'node:perf_hooks';
import { importWeb } from './_setup.mjs';

const S = await importWeb('js/store.js');
const { sampleGames } = await importWeb('js/mock-data.js');
const BUDGET_MS = 50;

const base = sampleGames(0);
const games = sampleGames(300 - base.length);

const SCENARIOS = [
  { section: 'all', sortBy: 'name' },
  { section: 'all', sortBy: 'size' },
  { section: 'all', sortBy: 'recent', query: 'jogo teste 1' },
  { section: 'favorites', sortBy: 'playtime' },
  { section: 'recent' },
  { section: 'platform:Steam', sortBy: 'added', genre: 'RPG' },
  { section: 'all', sortBy: 'name', quick: 'stale', query: 'á' },
  { section: 'collection:Competitivo', sortBy: 'name', quick: 'installed' },
];

test(`fixture is a 300-game library`, () => {
  assert.equal(games.length, 300);
});

for (const sc of SCENARIOS) {
  test(`selectVisible ${JSON.stringify(sc)} < ${BUDGET_MS} ms`, () => {
    for (let i = 0; i < 3; i++) S.selectVisible(games, sc); // warm-up (JIT, Intl.Collator)
    let worst = 0;
    let out;
    for (let i = 0; i < 10; i++) {
      const t = performance.now();
      out = S.selectVisible(games, sc);
      worst = Math.max(worst, performance.now() - t);
    }
    assert.ok(Array.isArray(out));
    assert.ok(worst < BUDGET_MS, `worst run ${worst.toFixed(1)} ms`);
  });
}

test(`derived sidebar data (counts, genres, featured) for 300 games < ${BUDGET_MS} ms`, () => {
  const run = () => { S.sectionCounts(games); S.platformCounts(games); S.collectionCounts(games, ['Competitivo']); S.genreList(games); S.pickFeatured(games); };
  run();
  const t = performance.now();
  run();
  const ms = performance.now() - t;
  assert.ok(ms < BUDGET_MS, `${ms.toFixed(1)} ms`);
});
