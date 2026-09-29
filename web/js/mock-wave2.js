// Dev-mode mock of the wave-2 bridge commands (store info, PCGamingWiki, install health, drives,
// automation, hotkey validation). Variants live in mock-variants.js. Shapes follow BridgeCommands.Wave2.cs.
import { GAME_INFO } from './mock-data.js';
import { variantCommands } from './mock-variants.js';

const GB = 1073741824;
const HOTKEY_RE = /^((Ctrl|Alt|Shift|Win)\+)+([A-Z0-9]|F([1-9]|1[0-9]|2[0-4])|Space|Enter|Tab|Up|Down|Left|Right|Home|End|PageUp|PageDown|Insert|Delete|Pause|PrintScreen|Num[0-9])$|^F([1-9]|1[0-9]|2[0-4])$/;
const TAKEN_HOTKEYS = ['Alt+F4', 'Win+D', 'Win+E', 'Win+L', 'Ctrl+Alt+Delete', 'Alt+Tab'];
const ACTION_TYPES = ['run', 'close', 'powerPlan', 'audioDevice', 'resolution', 'wait'];
const hash = (s) => [...String(s)].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7);
const clone = (o) => JSON.parse(JSON.stringify(o));

const OPTIONS = {
  powerPlans: [
    { id: '381b4222-f694-41f0-9685-ff5bb260df2e', name: 'Equilibrado', current: true },
    { id: '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c', name: 'Alto desempenho', current: false },
    { id: 'a1841308-3541-4fab-bc81-f71556f20b4a', name: 'Economia de energia', current: false },
  ],
  audioDevices: [
    { id: '{0.0.0.00000000}.{5f1c}', name: 'Alto-falantes (Realtek High Definition Audio)', current: true },
    { id: '{0.0.0.00000000}.{9a2e}', name: 'Headset (HyperX Cloud II)', current: false },
    { id: '{0.0.0.00000000}.{c07d}', name: 'LG ULTRAGEAR (NVIDIA High Definition Audio)', current: false },
  ],
  resolutions: ['2560x1440@165', '2560x1440@144', '1920x1080@144', '1920x1080@60', '1280x720@60']
    .map((id, i) => ({ id, name: `${id.replace('x', ' × ').replace('@', ' @ ')} Hz`, current: i === 0 })),
};

export function wave2Commands(ctx) {
  const { games, find, emitGames, event, addUndo, settings, ok, fail, wait } = ctx;
  const openable = new Set();
  const profiles = new Map();
  let defaultProfile = {
    enabled: true, useDefault: true, after: [],
    before: [{ type: 'powerPlan', target: OPTIONS.powerPlans[1].id, args: '', seconds: 0, enabled: true }],
  };
  const emptyProfile = () => ({ enabled: true, useDefault: true, before: [], after: [] });

  function uninstallInfo(g) {
    if (g.source === 'steam') return { method: 'steam', displayName: g.name };
    if (g.source === 'epic') return { method: 'epic', displayName: g.name };
    if (g.ext === '.exe' || g.ext === '.url' || g.broken) return { method: 'none', displayName: '' };
    return { method: 'registry', displayName: `${g.name}${g.platform !== 'PC' ? ` (${g.platform})` : ''}` };
  }

  function pcgwPaths(g) {
    const n = g.name.replace(/[:']/g, '');
    const h = hash(g.id);
    const mk = (raw, path, exists, kind) => {
      if (exists) openable.add(path);
      return { raw, path, exists, kind };
    };
    return {
      saveLocations: [
        mk(`{{p|userprofile}}\\Saved Games\\${n}`, `C:\\Users\\Demo\\Saved Games\\${n}`, h % 3 !== 0, 'save'),
        ...(h % 2 ? [mk(`{{p|steam}}\\userdata\\{{p|uid}}\\${g.steamAppId || '0'}\\remote`, '', false, 'save')] : []),
      ],
      configLocations: [mk(`{{p|localappdata}}\\${n}\\Saved\\Config\\Windows`, `C:\\Users\\Demo\\AppData\\Local\\${n}\\Saved\\Config\\Windows`, h % 4 !== 1, 'config')],
    };
  }

  function validateProfile(p) {
    for (const [list, label] of [[p?.before, 'Antes de jogar'], [p?.after, 'Depois de jogar']]) {
      if (!Array.isArray(list)) return 'Perfil de automação inválido.';
      for (const a of list) {
        if (!ACTION_TYPES.includes(a.type)) return `${label}: tipo de ação desconhecido.`;
        if (a.type !== 'wait' && !String(a.target || '').trim()) return `${label}: preencha todas as ações.`;
        if (a.type === 'wait' && !(a.seconds >= 1 && a.seconds <= 30)) return `${label}: a espera deve ser de 1 a 30 segundos.`;
      }
    }
    return null;
  }

  return {
    ...variantCommands(ctx),

    async getGameInfo({ id }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      await wait(450);
      const appId = g.steamAppId || '';
      const meta = settings.fetchMetadata && GAME_INFO[appId];
      const size = g.sizeBytes > 0 ? g.sizeBytes : g.installDir && !g.broken ? Math.round((1 + (hash(id) % 40)) * GB * 0.9) : -1;
      return {
        ok: true,
        data: {
          appId,
          info: meta ? { appId, ...meta, fetchedAt: new Date().toISOString() } : null,
          steam: g.source === 'steam' ? {
            appId, playtimeMinutes: Math.round(g.playSeconds / 60) + 95, lastPlayed: g.lastPlayed,
            updatePending: !!g.updatePending, sizeOnDisk: size, installDir: g.installDir,
          } : null,
          health: { broken: !!g.broken, reason: g.brokenReason || '' },
          uninstall: uninstallInfo(g),
          canValidate: g.source === 'steam' && !!appId,
          sizeBytes: size,
          pcgwUrl: appId ? `https://www.pcgamingwiki.com/api/appid.php?appid=${appId}` : `https://www.pcgamingwiki.com/w/index.php?search=${encodeURIComponent(g.name)}`,
        },
      };
    },

    async getPcgw({ id }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      await wait(700);
      const found = settings.pcgwEnabled && (!!g.steamAppId || g.source !== 'folder' || hash(id) % 3 !== 0);
      if (!found) return { ok: true, data: { found: false, pageUrl: '', title: '', saveLocations: [], configLocations: [] } };
      const title = g.name.replace(/ /g, '_');
      return { ok: true, data: { found: true, pageUrl: `https://www.pcgamingwiki.com/wiki/${encodeURIComponent(title)}`, title: g.name, ...pcgwPaths(g) } };
    },

    openPath({ path }) {
      if (!openable.has(path)) return fail('Este caminho não pode ser aberto.');
      console.info('[mock] openPath', path);
      return ok('Abrindo no Explorador de Arquivos.');
    },

    validateGame({ id }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      if (g.source !== 'steam') return fail('A verificação de arquivos só está disponível para jogos da Steam.');
      return ok(`A Steam vai verificar os arquivos de ${g.name}.`, id);
    },

    uninstallGame({ id }) {
      const g = find(id);
      if (!g) return fail('Jogo não encontrado.');
      const un = uninstallInfo(g);
      if (un.method === 'none') return fail(`Não encontrei um desinstalador para ${g.name}. Use Configurações do Windows › Aplicativos.`);
      const msg = { steam: 'Abrindo a desinstalação na Steam…', epic: 'Abrindo a biblioteca da Epic Games…' }[un.method] || `Abrindo o desinstalador de ${un.displayName}…`;
      return ok(msg, id);
    },

    async getDrives() {
      await wait(200);
      return {
        ok: true,
        data: {
          drives: [
            { name: 'C:\\', label: 'Windows', totalBytes: 476 * GB, freeBytes: 61 * GB },
            { name: 'D:\\', label: 'Jogos', totalBytes: 931 * GB, freeBytes: 38 * GB },
            { name: 'E:\\', label: '', totalBytes: 1863 * GB, freeBytes: 1320 * GB },
          ],
        },
      };
    },

    cleanupBroken() {
      const broken = games.filter((g) => g.broken);
      const results = broken.map((g) => {
        const i = games.indexOf(g);
        games.splice(i, 1);
        const token = addUndo(() => { games.splice(Math.min(i, games.length), 0, g); });
        return { ok: true, message: `${g.name} foi removido.`, undoToken: token, gameId: g.id };
      });
      emitGames();
      return { ok: true, data: { results } };
    },

    getGenres() {
      const set = new Set(games.flatMap((g) => g.genres || []));
      return { ok: true, data: { genres: [...set].sort((a, b) => a.localeCompare(b, 'pt-BR')) } };
    },

    getAutomation({ id }) {
      if (!id) return { ok: true, data: clone(defaultProfile) };
      if (!find(id)) return fail('Jogo não encontrado.');
      return { ok: true, data: clone(profiles.get(id) || emptyProfile()) };
    },

    saveAutomation({ id, profile }) {
      const err = validateProfile(profile);
      if (err) return fail(err);
      const p = {
        enabled: profile.enabled !== false, useDefault: profile.useDefault !== false,
        before: profile.before.map((a) => ({ ...a, seconds: Math.max(0, Math.min(30, a.seconds | 0)) })),
        after: profile.after.map((a) => ({ ...a, seconds: Math.max(0, Math.min(30, a.seconds | 0)) })),
      };
      if (!id) { defaultProfile = p; return ok('Automação padrão salva.'); }
      if (!find(id)) return fail('Jogo não encontrado.');
      profiles.set(id, p);
      return ok('Automação salva.', id);
    },

    async automationOptions() { await wait(250); return { ok: true, data: clone(OPTIONS) }; },

    // Overrides the wave-1 mock: validates hotkeys like SettingsStore and simulates a taken combination.
    setSettings({ patch = {} }) {
      for (const k of ['hotkey', 'quickLaunchHotkey']) {
        if (k in patch && !HOTKEY_RE.test(String(patch[k]))) return fail('Atalho inválido. Use algo como Ctrl+Alt+G.');
      }
      Object.assign(settings, patch);
      event('settings', settings);
      for (const k of ['hotkey', 'quickLaunchHotkey']) {
        if (k in patch && TAKEN_HOTKEYS.includes(patch[k])) {
          setTimeout(() => event('toast', { text: `O atalho ${patch[k]} já está em uso por outro programa. Escolha outra combinação.`, kind: 'err' }), 250);
        }
      }
      return { ok: true, data: settings };
    },
  };
}
