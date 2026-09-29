// In-page mock of the C# host (dev mode only). Mirrors window.chrome.webview's surface:
// postMessage, postMessageWithAdditionalObjects, addEventListener('message').
import { sampleGames, makeArt, STEAM_CATALOG } from './mock-data.js';
import { DEFAULT_SETTINGS } from './store.js';
import { featureCommands } from './mock-features.js';

const LATENCY = 60;
const ok = (message, gameId = null, undoToken = null) => ({ ok: true, data: { message, gameId, undoToken } });
const fail = (error) => ({ ok: false, error });
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const norm = (s) => String(s).normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();

export function createMockHost() {
  const params = new URLSearchParams(globalThis.location?.search || '');
  const listeners = new Set();
  const games = sampleGames(Number(params.get('many')) || 0);
  const defaults = new Map(games.map((g) => [g.id, { name: g.name, art: { ...g.art } }]));
  let collections = ['Competitivo', 'Single-player', 'Com amigos'];
  const settings = { ...DEFAULT_SETTINGS, gamesDir: 'C:\\Users\\Demo\\Desktop\\jogos', updateRepo: 'demo/gameshub' };
  const windowState = { maximized: false, fullscreen: false, pinned: false };
  const undoStack = new Map(); let undoSeq = 0, emitTimer = null;

  const send = (msg) => setTimeout(() => {
    const clone = JSON.parse(JSON.stringify(msg));
    for (const fn of listeners) fn({ data: clone });
  }, LATENCY);
  const event = (name, data) => send({ type: 'event', name, data });
  const snapshot = () => ({ games, collections });
  const emitGames = () => { clearTimeout(emitTimer); emitTimer = setTimeout(() => event('games', snapshot()), 150); };
  const find = (id) => games.find((g) => g.id === id);
  const addUndo = (fn) => { const t = `u${++undoSeq}`; undoStack.set(t, fn); return t; };

  function newGame(id, name, platform, source, ext) {
    const hue = (name.length * 47) % 360;
    const g = {
      id, name, platform, source, ext, filePath: source === 'folder' ? `C:\\Users\\Demo\\Desktop\\jogos\\${name}${ext}` : '',
      installDir: '', launchArgs: '', steamAppId: source === 'steam' ? id.slice(6) : '', favorite: false, hidden: false,
      collections: [], lastPlayed: null, playSeconds: 0, addedAt: new Date().toISOString(), running: false,
      sizeBytes: 0, updatePending: false, broken: false, brokenReason: '', genres: [], variants: [],
      art: makeArt(name, `hsl(${hue} 50% 40%)`, `hsl(${(hue + 70) % 360} 50% 22%)`, name.length,
        source === 'steam' ? ['header', 'capsule', 'hero', 'logo', 'icon'] : ['icon']),
    };
    games.push(g);
    defaults.set(id, { name, art: { ...g.art } });
    return g;
  }

  function addPath(fileName) {
    const m = /^(.*?)(\.(exe|lnk|url))$/i.exec(fileName);
    if (!m) return { ok: false, message: `"${fileName}" não é um atalho ou executável (.exe, .lnk, .url).` };
    const id = `folder:${fileName.toLowerCase()}`;
    if (find(id)) return { ok: false, message: `"${m[1]}" já está na biblioteca.`, gameId: id };
    newGame(id, m[1], 'PC', 'folder', m[2].toLowerCase());
    return { ok: true, message: `"${m[1]}" adicionado à biblioteca.`, gameId: id, undoToken: null };
  }

  const commands = {
    getState: () => ({ ok: true, data: { games, collections, settings, version: '2.0.0-demo', windowState, demo: true } }),

    launch({ id, variantId }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      if (g.running) return fail(`${g.name} já está em execução.`);
      if (g.broken) return fail(`Não foi possível iniciar ${g.name}: ${g.brokenReason || 'atalho quebrado'}.`);
      const variant = (g.variants || []).find((v) => v.id === variantId);
      g.running = true; g.lastPlayed = new Date().toISOString();
      event('running', { id, running: true }); emitGames();
      setTimeout(() => {
        g.running = false; g.playSeconds += 20 * 60;
        event('running', { id, running: false }); emitGames();
      }, 12000);
      return ok(`Iniciando ${g.name}${variant ? ` (${variant.label})` : ''}…`, id);
    },

    reveal({ id }) { const g = find(id); return g ? ok(`Abrindo o local de ${g.name}.`, id) : fail('Jogo não encontrado.'); },

    remove({ id }) {
      const i = games.findIndex((g) => g.id === id);
      if (i < 0) return fail('Jogo não encontrado.');
      const [g] = games.splice(i, 1);
      emitGames();
      const token = addUndo(() => { games.splice(Math.min(i, games.length), 0, g); });
      return ok(g.source === 'folder' ? `${g.name} foi movido para a lixeira.` : `${g.name} foi removido da biblioteca.`, id, token);
    },

    undo({ token }) {
      const fn = undoStack.get(token);
      if (!fn) return fail('Não há nada para desfazer.');
      undoStack.delete(token); fn(); emitGames();
      return ok('Ação desfeita.');
    },

    updateGame({ id, edit = {} }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      const before = JSON.parse(JSON.stringify(g));
      if (edit.name != null) g.name = edit.name.trim() || defaults.get(id)?.name || g.name;
      if (edit.launchArgs != null) g.launchArgs = edit.launchArgs;
      if (edit.steamAppId != null) {
        g.steamAppId = edit.steamAppId;
        if (edit.steamAppId && edit.steamAppId !== before.steamAppId) {
          setTimeout(() => { g.art = makeArt(g.name, '#335', '#a36', Number(edit.steamAppId) % 97, ['header', 'capsule', 'hero', 'logo', 'icon']); emitGames(); }, 900);
        }
      }
      if (edit.favorite != null) g.favorite = !!edit.favorite;
      if (edit.hidden != null) g.hidden = !!edit.hidden;
      if (edit.collections) {
        g.collections = [...new Set(edit.collections)];
        for (const c of g.collections) if (!collections.includes(c)) collections = [...collections, c];
      }
      emitGames();
      const token = edit.hidden === true ? addUndo(() => { g.hidden = false; }) : null;
      const msg = edit.hidden === true ? `${g.name} foi ocultado.` : edit.hidden === false ? `${g.name} voltou para a biblioteca.`
        : edit.favorite != null ? (g.favorite ? `${g.name} adicionado aos favoritos.` : `${g.name} removido dos favoritos.`)
          : 'Alterações salvas.';
      return ok(msg, id, token);
    },

    addFiles(_args, files) {
      if (!files.length) return fail('Nenhum arquivo recebido.');
      const results = files.map((f) => addPath(f.name || String(f)));
      emitGames();
      return { ok: true, data: { results } };
    },

    pickFile() {
      const n = games.filter((g) => g.id.startsWith('folder:exemplo')).length + 1;
      const r = addPath(`Exemplo ${n}.exe`);
      emitGames();
      return r.ok ? ok(r.message, r.gameId) : fail(r.message);
    },

    addSteam({ appId, name }) {
      if (!/^\d+$/.test(String(appId || ''))) return fail('App ID inválido.');
      const id = `steam:${appId}`;
      if (find(id)) return fail('Esse jogo já está na biblioteca.');
      const known = STEAM_CATALOG.find(([a]) => a === String(appId));
      const g = newGame(id, name || (known ? known[1] : `Steam App ${appId}`), 'Steam', 'steam', '');
      emitGames();
      return ok(`${g.name} adicionado à biblioteca.`, id);
    },

    async searchSteam({ query }) {
      await wait(350);
      const q = norm(query || '').trim();
      const results = STEAM_CATALOG
        .filter(([a, n]) => q && (a === q || norm(n).includes(q)))
        .slice(0, 12)
        .map(([appId, name]) => ({ appId, name, iconUrl: makeArt(name, '#2a4d7a', '#1a2233', Number(appId) % 50, ['icon']).icon }));
      return { ok: true, data: { results } };
    },

    setArt({ id, kind }, files) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      if (files[0]) {
        if (!/^image\//.test(files[0].type || '')) return fail('Escolha um arquivo de imagem (PNG, JPG ou WEBP).');
        g.art = { ...g.art, [kind]: URL.createObjectURL(files[0]) };
      } else {
        const s = Math.floor(Math.random() * 1000);
        g.art = { ...g.art, [kind]: makeArt(g.name, `hsl(${s % 360} 55% 40%)`, '#141826', s, [kind])[kind] };
      }
      emitGames();
      return ok('Imagem atualizada.', id);
    },

    clearArt({ id, kind }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      g.art = { ...g.art, [kind]: defaults.get(id)?.art[kind] ?? null };
      emitGames();
      return ok('Imagem restaurada.', id);
    },

    refreshArt({ id }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      setTimeout(() => { g.art = { ...defaults.get(id).art }; emitGames(); }, 800);
      return ok(`Buscando artes de ${g.name}…`, id);
    },

    async rescan() { await wait(600); emitGames(); return ok(`Biblioteca atualizada: ${games.length} jogos.`); },

    setSettings({ patch = {} }) {
      Object.assign(settings, patch);
      event('settings', settings);
      return { ok: true, data: settings };
    },

    async pickGamesDir() {
      await wait(200);
      settings.gamesDir = 'D:\\Jogos';
      event('settings', settings);
      return { ok: true, data: settings };
    },

    window({ action, on }) {
      if (action === 'maximize') windowState.maximized = true;
      else if (action === 'restore') windowState.maximized = false;
      else if (action === 'pin') windowState.pinned = on ?? !windowState.pinned;
      else if (action === 'fullscreen') windowState.fullscreen = on ?? !windowState.fullscreen;
      else if (action === 'close' || action === 'minimize') console.info(`[mock] window ${action}`);
      event('windowState', windowState);
      return { ok: true, data: windowState };
    },

    openExternal({ url }) {
      if (!/^https:\/\//i.test(url || '')) return fail('Somente links https são permitidos.');
      console.info('[mock] openExternal', url);
      return { ok: true, data: {} };
    },

    checkUpdate: async () => {
      await wait(500);
      return { ok: true, data: { available: true, version: '2.1.0', notes: 'Melhorias de desempenho e correções.', pageUrl: 'https://github.com/demo/gameshub/releases' } };
    },

    installUpdate() {
      let p = 0;
      const t = setInterval(() => { p = Math.min(100, p + 7); event('updateProgress', { percent: p }); if (p >= 100) clearInterval(t); }, 180);
      return { ok: true, data: { started: true } };
    },

    log({ level = 'info', msg }) { (console[level] || console.log)('[ui]', msg); return { ok: true, data: {} }; },
    quit: () => ({ ok: true, data: {} }), openDataFolder: () => ({ ok: true, data: {} }),
  };
  Object.assign(commands, featureCommands({ games, find, emitGames, event, addUndo, settings, ok, fail, wait }));

  async function handle(msg, files) {
    if (!msg || msg.type !== 'cmd') return;
    const fn = commands[msg.name];
    let res;
    try { res = fn ? await fn(msg.args || {}, files) : fail(`Comando desconhecido: ${msg.name}`); }
    catch (err) { console.error('[mock]', err); res = fail('Erro interno na demonstração.'); }
    send({ type: 'reply', id: msg.id, ...res });
    if (msg.name === 'getState' && settings.checkUpdates) {
      setTimeout(() => event('update', { version: '2.1.0', notes: 'Melhorias de desempenho e correções.', pageUrl: 'https://github.com/demo/gameshub/releases' }), 6000);
    }
  }

  return {
    isMock: true,
    commands: Object.keys(commands),
    addEventListener(type, fn) { if (type === 'message') listeners.add(fn); },
    removeEventListener(type, fn) { listeners.delete(fn); },
    postMessage(msg) { handle(msg, []); },
    postMessageWithAdditionalObjects(msg, objs) { handle(msg, Array.from(objs || [])); },
  };
}
