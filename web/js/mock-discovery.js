// Dev-mode mock of game discovery ("Encontrar jogos no PC"): discoverGames / addDiscovered, with progress events.
// Mirrors Integrations/Discovery (DiscoveredGame fields) and the C# allowlist: only executables returned by the last
// discovery can be added.
const GB = 1073741824;
const key = (s) => String(s || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '');

const cand = (name, exe, source, confidence, reasons, sizeBytes = -1, publisher = '') => ({
  name, exe, installDir: exe.slice(0, exe.lastIndexOf('\\')), source, confidence, reasons, sizeBytes, publisher,
});

/** Realistic candidates (the ones already in the demo library are filtered out, like the C# side does). */
export const DEMO_CANDIDATES = [
  cand('Hollow Knight', 'D:\\Jogos\\Hollow Knight\\hollow_knight.exe', 'folder', 1,
    ['Unity', 'Pasta de dados Unity', 'Steamworks', 'Pasta de jogos']),
  cand('Hades', 'C:\\Program Files (x86)\\Hades\\x64\\Hades.exe', 'registry', 0.95,
    ['FMOD', 'Bink Video', 'Editora de jogos', 'Instalação grande', 'Registro do Windows'], 15 * GB, 'Supergiant Games'),
  cand('The Witcher 3: Wild Hunt', 'D:\\GOG Games\\The Witcher 3\\bin\\x64\\witcher3.exe', 'registry', 1,
    ['GOG', 'GOG Galaxy SDK', 'Pasta de jogos', 'Registro do Windows'], 48 * GB, 'CD PROJEKT RED'),
  cand('Forza Horizon 5', 'E:\\XboxGames\\Forza Horizon 5\\Content\\gamelaunchhelper.exe', 'xbox', 0.8,
    ['Xbox / Microsoft Store', 'Pasta de jogos']),
  cand('TEKKEN 7', 'D:\\Games\\TEKKEN 7\\TEKKEN 7.exe', 'folder', 1,
    ['Unreal (-Shipping.exe)', 'Unreal Engine', 'Pacotes .pak', 'Pasta de jogos']),
  cand('Celeste', 'D:\\Jogos\\Celeste\\Celeste.exe', 'folder', 0.85, ['MonoGame/FNA', 'Steamworks', 'Pasta de jogos']),
  cand('Call of Duty - Black Ops 2', 'D:\\Games\\Call of Duty - Black Ops 2\\t6mp.exe', 'folder', 0.7,
    ['Steamworks', 'Bink Video', 'Pasta de jogos']),
  cand('PointBlank', 'D:\\Zepetto\\PointBlank\\PointBlank.exe', 'folder', 0.65, ['FMOD', 'PhysX', 'DirectX 9', 'Pacotes .pak']),
  cand('Retro Arcade Pack', 'C:\\Program Files\\Retro Arcade Pack\\arcade.exe', 'registry', 0.55,
    ['XInput', 'OpenAL', 'Editora de jogos', 'Registro do Windows'], 2 * GB, 'Pixel Games'),
];

export function discoveryCommands({ games, emitGames, event, newGame, fail, wait }) {
  let allowed = new Map(); // exe (lowercase) -> candidate of the last discovery
  let running = false;

  return {
    async discoverGames() {
      if (running) return fail('Uma busca já está em andamento.');
      running = true;
      try {
        for (const text of ['Lendo os programas instalados…', 'Procurando em D:\\Jogos…', 'Procurando em E:\\XboxGames…', 'Analisando 124 pastas…']) {
          event('discoverProgress', { text });
          await wait(220);
        }
        const known = new Set(games.map((g) => key(g.name)));
        const candidates = DEMO_CANDIDATES.filter((c) => !known.has(key(c.name))).map((c) => ({ ...c, reasons: [...c.reasons] }));
        allowed = new Map(candidates.map((c) => [c.exe.toLowerCase(), c]));
        event('discoverProgress', { text: 'Concluído.' });
        return { ok: true, data: { candidates } };
      } finally { running = false; }
    },

    addDiscovered({ items } = {}) {
      if (!Array.isArray(items) || !items.length) return fail('Nenhum jogo selecionado.');
      const seen = new Set();
      const results = [];
      for (const it of items.slice(0, 200)) {
        if (!it || typeof it !== 'object') continue;
        const k = String(it.exe || '').toLowerCase();
        if (seen.has(k)) continue;
        seen.add(k);
        const c = allowed.get(k);
        if (!c) { results.push({ ok: false, message: 'Este jogo não faz parte da última busca. Busque novamente.', gameId: null, undoToken: null }); continue; }
        const name = String(it.name ?? '').replace(/[\u0000-\u001f]/g, '').trim().slice(0, 120) || c.name;
        const id = `folder:${name.toLowerCase()}.lnk`;
        if (games.some((g) => g.id === id)) { results.push({ ok: true, message: `${name} já está na biblioteca.`, gameId: id, undoToken: null }); continue; }
        const g = newGame(id, name, 'PC', 'folder', '.lnk');
        g.installDir = c.installDir;
        results.push({ ok: true, message: `${name} adicionado à biblioteca.`, gameId: id, undoToken: null });
      }
      if (!results.length) return fail('Nenhum jogo selecionado.');
      emitGames();
      return { ok: true, data: { results } };
    },
  };
}
