// GamesHub E2E - UI checks over the WebView2 DevTools protocol (CDP).
// Zero dependencies: Node >= 22 (global fetch + WebSocket).
//
//   node cdp.mjs --port 9333 --out dist/e2e --expect-games 5 --timeout 60
//
// Observes ONLY the WebView2 instance started by Invoke-E2E.ps1 (its own --remote-debugging-port).
// Never sends OS input: navigation goes through the app's own JS actions (dynamic import of /js/actions.js).
// Prints one JSON line to stdout: { checks: [{ name, pass, detail, ms }], errors: [...] }.
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';

const MAIN_URL = 'https://app.gameshub.example/index.html';

function parseArgs(argv) {
  const o = { port: 0, out: 'dist/e2e', expectGames: 5, timeout: 60, prefix: '', filePrefix: '' };
  for (let i = 2; i < argv.length; i++) {
    const k = argv[i], v = argv[i + 1];
    if (k === '--port') { o.port = Number(v); i++; }
    else if (k === '--out') { o.out = v; i++; }
    else if (k === '--expect-games') { o.expectGames = Number(v); i++; }
    else if (k === '--timeout') { o.timeout = Number(v); i++; }
    else if (k === '--prefix') { o.prefix = v; i++; }
    else if (k === '--file-prefix') { o.filePrefix = v; i++; }
  }
  if (!o.port) throw new Error('--port is required');
  return o;
}

const opts = parseArgs(process.argv);
const deadline = (sec) => Date.now() + sec * 1000;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const checks = [];
const pageErrors = [];

async function check(name, fn) {
  const t0 = Date.now();
  let pass = false, detail = '';
  try {
    const r = await fn();
    pass = r?.pass !== false;
    detail = r?.detail ?? '';
  } catch (err) {
    pass = false;
    detail = String(err?.message || err);
  }
  checks.push({ name: opts.prefix + name, pass, detail: String(detail).slice(0, 600), ms: Date.now() - t0 });
  return pass;
}

// ------------------------------------------------------------------ CDP plumbing

async function findMainTarget(untilMs) {
  let last = '';
  while (Date.now() < untilMs) {
    try {
      const res = await fetch(`http://127.0.0.1:${opts.port}/json/list`);
      const list = await res.json();
      // The quick-launch palette (/quick/index.html) shares the environment: pick the main page only.
      const t = list.find((x) => x.type === 'page' && x.url.split('#')[0].split('?')[0] === MAIN_URL);
      if (t) return t;
      last = list.map((x) => `${x.type}:${x.url}`).join(', ') || '(no targets)';
    } catch (err) {
      last = String(err?.message || err);
    }
    await sleep(250);
  }
  throw new Error(`main page target not found: ${last}`);
}

class Cdp {
  constructor(wsUrl) {
    this.seq = 0;
    this.pending = new Map();
    this.listeners = [];
    this.ws = new WebSocket(wsUrl);
    this.ws.addEventListener('message', (e) => this.onMessage(JSON.parse(String(e.data))));
    this.closed = new Promise((r) => this.ws.addEventListener('close', r));
  }

  open() {
    return new Promise((resolve, reject) => {
      this.ws.addEventListener('open', resolve, { once: true });
      this.ws.addEventListener('error', (e) => reject(new Error('WebSocket error: ' + (e.message || 'connect failed'))), { once: true });
    });
  }

  onMessage(m) {
    if (m.id && this.pending.has(m.id)) {
      const p = this.pending.get(m.id);
      this.pending.delete(m.id);
      if (m.error) p.reject(new Error(`${p.method}: ${m.error.message}`));
      else p.resolve(m.result);
      return;
    }
    if (m.method) for (const l of this.listeners) l(m.method, m.params);
  }

  send(method, params = {}, timeoutMs = 30000) {
    const id = ++this.seq;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error(`${method}: CDP timeout`)); }, timeoutMs);
      this.pending.set(id, {
        method,
        resolve: (v) => { clearTimeout(timer); resolve(v); },
        reject: (e) => { clearTimeout(timer); reject(e); },
      });
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }

  on(fn) { this.listeners.push(fn); }

  /** Evaluates an async function body in the page; returns its JSON value. */
  async eval(body, timeoutMs = 30000) {
    const expression = `(async () => { ${body} })()`;
    const r = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, timeoutMs);
    if (r.exceptionDetails) {
      const d = r.exceptionDetails;
      throw new Error('page eval: ' + (d.exception?.description || d.text));
    }
    return r.result.value;
  }

  close() { try { this.ws.close(); } catch { /* already closed */ } }
}

function describeConsole(p) {
  const args = (p.args || []).map((a) => a.value ?? a.description ?? a.type).join(' ');
  const f = p.stackTrace?.callFrames?.[0];
  return `console.${p.type}: ${args}${f ? ` @ ${f.url}:${f.lineNumber + 1}` : ''}`;
}

function watchErrors(cdp) {
  cdp.on((method, p) => {
    if (method === 'Runtime.exceptionThrown') {
      const d = p.exceptionDetails;
      pageErrors.push(`exception: ${d.exception?.description || d.text} @ ${d.url || ''}:${(d.lineNumber ?? 0) + 1}`);
    } else if (method === 'Runtime.consoleAPICalled' && (p.type === 'error' || p.type === 'assert')) {
      pageErrors.push(describeConsole(p));
    } else if (method === 'Log.entryAdded' && p.entry.level === 'error') {
      pageErrors.push(`log.${p.entry.source}: ${p.entry.text}${p.entry.url ? ' @ ' + p.entry.url : ''}`);
    }
  });
}

async function poll(cdp, body, untilMs, what) {
  let last;
  while (Date.now() < untilMs) {
    try {
      last = await cdp.eval(body);
      if (last && last.ok) return last;
    } catch (err) {
      last = { error: String(err.message || err) };
    }
    await sleep(200);
  }
  throw new Error(`${what} (last: ${JSON.stringify(last)?.slice(0, 300)})`);
}

async function screenshot(cdp, name) {
  await cdp.eval('await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))); return true;');
  const { data } = await cdp.send('Page.captureScreenshot', { format: 'png' });
  const file = join(opts.out, `${opts.filePrefix}${name}.png`);
  writeFileSync(file, Buffer.from(data, 'base64'));
  return file;
}

// Page-side snippets ------------------------------------------------------------
const IMPORTS = `const { store } = await import('/js/store.js'); const A = await import('/js/actions.js');`;
const VISIBLE = (sel) => `(() => { const e = document.querySelector(${JSON.stringify(sel)}); return !!e && !e.hidden && e.getClientRects().length > 0; })()`;

// ------------------------------------------------------------------ the suite

async function main() {
  mkdirSync(opts.out, { recursive: true });
  const until = deadline(opts.timeout);
  let target, cdp;

  if (!await check('cdp: main page target found', async () => {
    target = await findMainTarget(until);
    return { detail: target.url };
  })) return;

  if (!await check('cdp: attached', async () => {
    cdp = new Cdp(target.webSocketDebuggerUrl);
    await cdp.open();
    watchErrors(cdp);
    // Enabling replays console messages/exceptions already buffered by the page since it loaded.
    await cdp.send('Runtime.enable');
    await cdp.send('Log.enable');
    await cdp.send('Page.enable');
    return { detail: target.webSocketDebuggerUrl };
  })) return;

  try {
    await runSuite(cdp, until);
  } finally {
    cdp.close();
  }
}

async function runSuite(cdp, until) {
  const N = opts.expectGames;

  await check('ui: reaches ready state', async () => {
    const r = await poll(cdp, `${IMPORTS} const s = store.state;
      return { ok: s.status === 'ready' && !!A.getBridge() && !A.getBridge().isMock, status: s.status, error: s.error, version: s.version, demo: s.demo };`,
    until, 'store.status never became ready');
    return { detail: `version ${r.version}, demo=${r.demo}` };
  });

  let games = [];
  await check(`ui: store has ${N} games`, async () => {
    const r = await poll(cdp, `${IMPORTS} const g = store.state.games;
      return { ok: g.length === ${N}, n: g.length, games: g.map((x) => ({ id: x.id, name: x.name, ext: x.ext, platform: x.platform, broken: !!x.broken, brokenReason: x.brokenReason || '', icon: x.art && x.art.icon })) };`,
    until, `expected ${N} games`);
    games = r.games;
    return { detail: games.map((g) => `${g.name} [${g.ext}/${g.platform}]`).join('; ') };
  });

  let broken;
  await check('ui: broken shortcut flagged', async () => {
    const r = await poll(cdp, `${IMPORTS} const b = store.state.games.filter((g) => g.broken);
      const card = b[0] && document.querySelector('.card[data-game="' + CSS.escape(b[0].id) + '"]');
      return { ok: b.length === 1 && /ghost/i.test(b[0].name) && !!card && card.classList.contains('is-broken'),
               broken: b.map((g) => g.name + ' (' + (g.brokenReason || '') + ')'), id: b[0] && b[0].id, card: !!card };`,
    until, 'exactly one broken game (the Ghost shortcut) with an is-broken card');
    broken = r.id;
    return { detail: r.broken.join('; ') };
  });

  await check('ui: library DOM lists every game', async () => {
    const r = await poll(cdp, `return { ok: ${VISIBLE('#view-library')} && document.querySelectorAll('#view-library .card[data-game]').length === ${N},
      cards: document.querySelectorAll('#view-library .card[data-game]').length, title: document.querySelector('.lib-title')?.textContent };`,
    until, 'library view with all cards');
    return { detail: `${r.cards} cards, title "${r.title}"` };
  });
  await check('ui: screenshot library', async () => ({ detail: await screenshot(cdp, 'library') }));

  const target = games.find((g) => !g.broken && g.ext === '.lnk') || games.find((g) => !g.broken) || games[0];
  await check('ui: navigate to game details', async () => {
    if (!target) throw new Error('no game to open');
    await cdp.eval(`${IMPORTS} A.openDetails(${JSON.stringify(target.id)}); return true;`);
    const r = await poll(cdp, `${IMPORTS} return { ok: store.state.route.view === 'game' && ${VISIBLE('#view-game')} && ${VISIBLE('#view-game .details-main')}
      && !document.querySelector('#view-library').offsetParent
      && (document.querySelector('#view-game .hero-title') || {}).textContent === ${JSON.stringify(target.name)},
      route: store.state.route, heading: (document.querySelector('#view-game .hero-title') || {}).textContent };`,
    deadline(10), 'details view visible with the game title');
    return { detail: `${target.name} -> "${(r.heading || '').trim()}"` };
  });
  await check('ui: screenshot details', async () => ({ detail: await screenshot(cdp, 'details') }));

  if (broken) {
    await check('ui: broken game details show warning', async () => {
      await cdp.eval(`${IMPORTS} A.openDetails(${JSON.stringify(broken)}); return true;`);
      await poll(cdp, `return { ok: ${VISIBLE('#view-game [data-broken]')} };`, deadline(10), 'broken panel visible');
      return { detail: 'panel [data-broken] visible' };
    });
  }

  await check('ui: navigate to settings', async () => {
    await cdp.eval(`${IMPORTS} A.openSettings(); return true;`);
    const r = await poll(cdp, `${IMPORTS} const h = document.querySelector('#view-settings .settings h1');
      const path = document.querySelector('#view-settings [data-show="gamesDir"]');
      return { ok: store.state.route.view === 'settings' && ${VISIBLE('#view-settings')} && !!h && h.textContent.trim() === 'Configurações',
               h: h && h.textContent, gamesDir: path && path.textContent };`, deadline(10), 'settings view visible');
    return { detail: `"${r.h}", gamesDir ${r.gamesDir}` };
  });
  await check('ui: screenshot settings', async () => ({ detail: await screenshot(cdp, 'settings') }));

  await check('ui: back to library', async () => {
    await cdp.eval(`${IMPORTS} A.goLibrary(); return true;`);
    await poll(cdp, `${IMPORTS} return { ok: store.state.route.view === 'library' && ${VISIBLE('#view-library')}
      && document.querySelector('#view-game').hidden && document.querySelector('#view-settings').hidden
      && document.querySelectorAll('#view-library .card[data-game]').length === ${N} };`, deadline(10), 'library visible again');
    return { detail: 'library visible, details/settings hidden' };
  });

  // ---- bridge round-trips
  await check('bridge: getState', async () => {
    const r = await cdp.eval(`${IMPORTS} const s = await A.getBridge().call('getState', {});
      return { n: s.games.length, v: s.version, demo: s.demo, dir: s.settings && s.settings.gamesDir,
               offline: s.settings && !s.settings.autoArtwork && !s.settings.checkUpdates && !s.settings.hotkeyEnabled && !s.settings.quickLaunchEnabled };`);
    if (r.n !== N) throw new Error(`getState returned ${r.n} games`);
    if (r.demo) throw new Error('getState says demo=true');
    if (!r.offline) throw new Error('seeded offline settings were not applied');
    return { detail: `${r.n} games, version ${r.v}, gamesDir ${r.dir}` };
  });
  await check('bridge: getDrives', async () => {
    const r = await cdp.eval(`${IMPORTS} const d = await A.getBridge().call('getDrives', {});
      return (d.drives || []).map((x) => JSON.stringify(x).slice(0, 120));`);
    if (!r.length) throw new Error('no drives reported');
    return { detail: `${r.length} drive(s): ${r[0]}` };
  });
  await check('bridge: rescan', async () => {
    const r = await cdp.eval(`${IMPORTS} const d = await A.getBridge().call('rescan', {}); return d;`, 70000);
    const after = await poll(cdp, `${IMPORTS} return { ok: store.state.games.length === ${N}, n: store.state.games.length };`,
      deadline(15), 'game count after rescan');
    return { detail: `${r?.message ?? JSON.stringify(r)}; ${after.n} games after` };
  });
  await check('bridge: unknown command is rejected cleanly', async () => {
    const r = await cdp.eval(`${IMPORTS} try { await A.getBridge().call('e2eNoSuchCommand', {}, { timeout: 5000 }); return { ok: true }; }
      catch (e) { return { ok: false, code: e.code, msg: e.message }; }`);
    if (r.ok || r.code !== 'failed') throw new Error(`expected a failed reply, got ${JSON.stringify(r)}`);
    return { detail: r.msg };
  });

  // ---- art host
  await check('art host: serves an icon', async () => {
    // icon extraction is asynchronous: wait a little for the first icon URL
    let r;
    try {
      r = await poll(cdp, `${IMPORTS} const g = store.state.games.find((x) => x.art && x.art.icon);
        return { ok: !!g, url: g && g.art.icon, name: g && g.name };`, deadline(15), 'icon url');
    } catch {
      return { detail: 'skipped: no game exposes an icon URL (autoArtwork=false)' };
    }
    const f = await cdp.eval(`const res = await fetch(${JSON.stringify(r.url)}); const b = await res.arrayBuffer();
      return { status: res.status, type: res.headers.get('content-type'), bytes: b.byteLength };`);
    if (f.status !== 200 || f.bytes === 0) throw new Error(`${r.url} -> HTTP ${f.status}, ${f.bytes} bytes`);
    return { detail: `${r.name}: HTTP ${f.status} ${f.type} ${f.bytes} B` };
  });

  // ---- full boot observed from the first byte (errors before attach are replayed, this double-checks it)
  await check('ui: reload boots cleanly', async () => {
    await cdp.send('Page.reload', { ignoreCache: true });
    await sleep(300);
    const r = await poll(cdp, `${IMPORTS} return { ok: store.state.status === 'ready' && store.state.games.length === ${N}
      && document.querySelectorAll('#view-library .card[data-game]').length === ${N}, status: store.state.status, n: store.state.games.length };`,
    deadline(30), 'ready with all games after reload');
    return { detail: `${r.n} games after reload` };
  });

  await sleep(500); // let late console messages arrive
  await check('ui: no uncaught exceptions / console errors', async () => {
    if (pageErrors.length) throw new Error(`${pageErrors.length} error(s): ${pageErrors.slice(0, 5).join(' | ')}`);
    return { detail: 'none' };
  });
}

main()
  .catch((err) => checks.push({ name: opts.prefix + 'cdp: suite crashed', pass: false, detail: String(err?.stack || err), ms: 0 }))
  .finally(() => {
    process.stdout.write(JSON.stringify({ checks, errors: pageErrors }) + '\n');
    process.exit(0);
  });
