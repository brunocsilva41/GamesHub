// Sample library for dev mode. Artwork is generated inline as SVG data URIs (no network).

const svgUri = (svg) => 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
const escXml = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const FONT = "font-family=\"Segoe UI, Arial, sans-serif\"";

function bg(w, h, c1, c2, seed) {
  const r = (n) => ((seed * 9301 + n * 49297) % 233280) / 233280;
  const circles = [0, 1, 2].map((i) =>
    `<circle cx="${(r(i) * w).toFixed(0)}" cy="${(r(i + 7) * h).toFixed(0)}" r="${(h * (0.3 + r(i + 3) * 0.5)).toFixed(0)}" fill="#fff" opacity="${(0.05 + r(i + 5) * 0.08).toFixed(2)}"/>`).join('');
  return `<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/></linearGradient>` +
    `<linearGradient id="s" x1="0" y1="0" x2="0" y2="1"><stop offset=".45" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity=".55"/></linearGradient></defs>` +
    `<rect width="${w}" height="${h}" fill="url(#g)"/>${circles}<rect width="${w}" height="${h}" fill="url(#s)"/>`;
}

export function makeArt(name, c1, c2, seed, kinds) {
  const t = escXml(name);
  const all = {
    header: svgUri(`<svg xmlns="http://www.w3.org/2000/svg" width="460" height="215" viewBox="0 0 460 215">${bg(460, 215, c1, c2, seed)}<text x="22" y="190" ${FONT} font-size="30" font-weight="700" fill="#fff">${t}</text></svg>`),
    capsule: svgUri(`<svg xmlns="http://www.w3.org/2000/svg" width="600" height="900" viewBox="0 0 600 900">${bg(600, 900, c1, c2, seed + 1)}<text x="40" y="830" ${FONT} font-size="54" font-weight="700" fill="#fff">${t}</text></svg>`),
    hero: svgUri(`<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="620" viewBox="0 0 1920 620">${bg(1920, 620, c2, c1, seed + 2)}</svg>`),
    logo: svgUri(`<svg xmlns="http://www.w3.org/2000/svg" width="640" height="160" viewBox="0 0 640 160"><text x="0" y="112" ${FONT} font-size="92" font-weight="800" fill="#fff" letter-spacing="-2">${t}</text></svg>`),
    icon: svgUri(`<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64"><rect x="4" y="4" width="56" height="56" rx="14" fill="${c1}"/><text x="32" y="42" text-anchor="middle" ${FONT} font-size="26" font-weight="800" fill="#fff">${escXml(name.slice(0, 1))}</text></svg>`),
  };
  const art = { header: null, capsule: null, hero: null, logo: null, icon: null };
  for (const k of kinds) art[k] = all[k];
  return art;
}

const HOUR = 3600, DAY = 86400000, GB = 1073741824;
const ago = (ms) => new Date(Date.now() - ms).toISOString();
const FULL = ['header', 'capsule', 'hero', 'logo', 'icon'];

// [id, name, platform, source, ext, colors, art kinds, extra]
const SAMPLES = [
  ['steam:730', 'Counter-Strike 2', 'Steam', 'steam', '', ['#c77a1b', '#2b3a55'], FULL, { genres: ['Tiro', 'Competitivo'], sizeBytes: 34 * GB, steamAppId: '730', favorite: true, collections: ['Competitivo'], lastPlayed: ago(2 * 3600e3), playSeconds: 512 * HOUR }],
  ['steam:1091500', 'Cyberpunk 2077', 'Steam', 'steam', '', ['#d6c51f', '#b3244d'], FULL, { genres: ['RPG', 'Mundo aberto'], sizeBytes: 71 * GB, updatePending: true, steamAppId: '1091500', collections: ['Single-player'], lastPlayed: ago(3 * DAY), playSeconds: 64 * HOUR }],
  ['steam:1145350', 'Hades II', 'Steam', 'steam', '', ['#6b2fb3', '#c2412c'], ['header', 'capsule', 'icon'], { genres: ['Roguelike', 'Ação'], sizeBytes: 11 * GB, steamAppId: '1145350', favorite: true, lastPlayed: ago(9 * DAY), playSeconds: 18 * HOUR + 1800 }],
  ['steam:1245620', 'Elden Ring', 'Steam', 'steam', '', ['#9a7b2c', '#1f2533'], FULL, { genres: ['RPG', 'Ação'], sizeBytes: 49 * GB, steamAppId: '1245620', collections: ['Single-player'], running: true, lastPlayed: ago(40e3), playSeconds: 140 * HOUR }],
  ['epic:Fortnite', 'Fortnite', 'Epic', 'epic', '', ['#3b6fd6', '#8a3fd1'], ['header', 'hero', 'icon'], { genres: ['Tiro', 'Battle royale'], lastPlayed: ago(21 * DAY), playSeconds: 22 * HOUR }],
  ['epic:AlanWake2', 'Alan Wake 2', 'Epic', 'epic', '', ['#1f3b4d', '#0d1117'], ['icon'], {}],
  ['folder:valorant.lnk', 'VALORANT', 'Riot', 'folder', '.lnk', ['#e0435a', '#1a1f2e'], ['icon'], { genres: ['Tiro', 'Competitivo'], favorite: true, collections: ['Competitivo'], lastPlayed: ago(26 * 3600e3), playSeconds: 80 * HOUR }],
  ['folder:league of legends.lnk', 'League of Legends', 'Riot', 'folder', '.lnk', ['#c89b3c', '#0a1428'], ['header'], { collections: ['Competitivo'], lastPlayed: ago(45 * DAY), playSeconds: 300 * HOUR }],
  ['folder:roblox.url', 'Roblox', 'Roblox', 'folder', '.url', ['#2b8a4e', '#1b2230'], ['icon'], { lastPlayed: ago(5 * DAY), playSeconds: 5 * HOUR }],
  ['folder:minecraft.lnk', 'Minecraft', 'Minecraft', 'folder', '.lnk', ['#4f8a2b', '#6b4a2b'], FULL, { genres: ['Sandbox'], sizeBytes: 1.4 * GB, favorite: true, collections: ['Com amigos'], lastPlayed: ago(12 * DAY), playSeconds: 45 * HOUR }],
  ['folder:overwatch.lnk', 'Overwatch 2', 'Battle.net', 'folder', '.lnk', ['#f29b2c', '#2a3550'], ['header'], { collections: ['Com amigos', 'Competitivo'], playSeconds: 12 * HOUR, lastPlayed: ago(60 * DAY) }],
  ['folder:ea sports fc 25.lnk', 'EA SPORTS FC 25', 'EA', 'folder', '.lnk', ['#1fae6b', '#0e2a3b'], [], { genres: ['Esportes'], broken: true, brokenReason: 'O destino do atalho não existe mais (C:\\Program Files\\EA Games\\FC 25\\FC25.exe).', playSeconds: 3 * HOUR, lastPlayed: ago(90 * DAY) }],
  ['folder:assassins creed mirage.lnk', "Assassin's Creed Mirage", 'Ubisoft', 'folder', '.lnk', ['#b98a3b', '#2a1d14'], ['capsule'], { hidden: true }],
  ['folder:the witcher 3.lnk', 'The Witcher 3: Wild Hunt', 'GOG', 'folder', '.lnk', ['#8c2f2f', '#1c1c24'], FULL, { genres: ['RPG', 'Mundo aberto'], sizeBytes: 50 * GB, steamAppId: '292030', collections: ['Single-player'], lastPlayed: ago(120 * DAY), playSeconds: 90 * HOUR }],
  ['folder:retroarch.exe', 'RetroArch', 'PC', 'folder', '.exe', ['#4a4f6b', '#1c1f2e'], ['icon'], {}],
  // wave 2: a variant member (grouped under Minecraft by mock-wave2), a suggestion pair and a second broken shortcut
  ['folder:minecraft bedrock.lnk', 'Minecraft Bedrock', 'Minecraft', 'folder', '.lnk', ['#4f8a2b', '#2b4a6b'], ['icon'], { genres: ['Sandbox'], playSeconds: 4 * HOUR, lastPlayed: ago(30 * DAY) }],
  ['folder:the witcher 3 dx11.lnk', 'The Witcher 3: Wild Hunt DirectX 11', 'GOG', 'folder', '.lnk', ['#8c2f2f', '#1c1c24'], ['icon'], { genres: ['RPG', 'Mundo aberto'] }],
  ['folder:need for speed heat.lnk', 'Need for Speed Heat', 'EA', 'folder', '.lnk', ['#d9480f', '#1b1b2f'], [], { genres: ['Corrida'], broken: true, brokenReason: 'A pasta de instalação não existe mais (D:\\Jogos\\NFS Heat).', lastPlayed: ago(200 * DAY), playSeconds: 11 * HOUR }],
];

/** Store metadata for the mock getGameInfo (keyed by Steam App ID). */
export const GAME_INFO = {
  730: { genres: ['Ação', 'Gratuito para jogar'], categories: ['Multijogador', 'Competitivo online', 'Suporte a controle'], shortDescription: 'Por mais de duas décadas, o Counter-Strike oferece uma experiência competitiva de elite. Counter-Strike 2 é o próximo capítulo.', releaseDate: '21 ago. 2012', developers: ['Valve'], publishers: ['Valve'], metacritic: 83, website: 'https://www.counter-strike.net/', controllerSupport: false },
  1091500: { genres: ['RPG', 'Mundo aberto'], categories: ['Um jogador', 'Conquistas Steam', 'Suporte total a controle'], shortDescription: 'Cyberpunk 2077 é um RPG de ação e aventura em mundo aberto ambientado em Night City, uma megalópole obcecada por poder, glamour e modificações corporais.', releaseDate: '9 dez. 2020', developers: ['CD PROJEKT RED'], publishers: ['CD PROJEKT RED'], metacritic: 86, website: 'https://www.cyberpunk.net', controllerSupport: true },
  1245620: { genres: ['Ação', 'RPG'], categories: ['Um jogador', 'Multijogador online', 'Suporte total a controle'], shortDescription: 'O NOVO RPG DE AÇÃO E FANTASIA. Levante-se, Maculado, e seja guiado pela graça para portar o poder do Anel Prístino.', releaseDate: '24 fev. 2022', developers: ['FromSoftware, Inc.'], publishers: ['FromSoftware, Inc.', 'Bandai Namco Entertainment'], metacritic: 94, website: 'https://www.eldenring.com', controllerSupport: true },
  1145350: { genres: ['Ação', 'Indie', 'RPG'], categories: ['Um jogador', 'Suporte total a controle'], shortDescription: 'Lute além do Submundo usando feitiçaria sombria para enfrentar o Titã do Tempo nesta sequência do aclamado roguelike.', releaseDate: '6 mai. 2024', developers: ['Supergiant Games'], publishers: ['Supergiant Games'], metacritic: 0, website: '', controllerSupport: true },
  292030: { genres: ['RPG'], categories: ['Um jogador', 'Suporte total a controle'], shortDescription: 'Você é Geralt de Rívia, caçador de monstros. O continente está em guerra e você precisa encontrar a criança da profecia.', releaseDate: '18 mai. 2015', developers: ['CD PROJEKT RED'], publishers: ['CD PROJEKT RED'], metacritic: 93, website: 'https://www.thewitcher.com', controllerSupport: true },
};

export function sampleGames(extra = 0) {
  const games = SAMPLES.map(([id, name, platform, source, ext, [c1, c2], kinds, x], i) => ({
    id, name, platform, source, ext,
    filePath: source === 'folder' ? `C:\\Users\\Demo\\Desktop\\jogos\\${id.slice(7)}` : '',
    installDir: `C:\\Games\\${name.replace(/[^\w ]/g, '')}`,
    launchArgs: '', steamAppId: '', favorite: false, hidden: false, collections: [],
    lastPlayed: null, playSeconds: 0, addedAt: ago((i + 1) * 7 * DAY), running: false,
    // wave-2 fields
    sizeBytes: 0, updatePending: false, broken: false, brokenReason: '', genres: [], variants: [],
    art: makeArt(name, c1, c2, i + 1, kinds),
    ...x,
  }));
  const platforms = ['Steam', 'Epic', 'PC', 'GOG', 'Riot'];
  for (let i = 0; i < extra; i++) {
    const p = platforms[i % platforms.length];
    const name = `Jogo de teste ${String(i + 1).padStart(3, '0')}`;
    games.push({
      id: `folder:test-${i}`, name, platform: p, source: 'folder', ext: '.lnk',
      filePath: `C:\\Demo\\${name}.lnk`, installDir: '', launchArgs: '', steamAppId: '',
      favorite: i % 17 === 0, hidden: false, collections: [], running: false,
      lastPlayed: i % 5 === 0 ? ago(i * 3600e3) : null, playSeconds: (i * 977) % 200000,
      addedAt: ago(i * 3600e3),
      sizeBytes: ((i * 7919) % 90) * GB, updatePending: i % 23 === 0, broken: i % 41 === 0,
      brokenReason: i % 41 === 0 ? 'Arquivo não encontrado.' : '', genres: [['Ação', 'RPG', 'Estratégia', 'Indie'][i % 4]], variants: [],
      art: makeArt(name, `hsl(${(i * 37) % 360} 45% 38%)`, `hsl(${(i * 37 + 60) % 360} 50% 22%)`, i + 50,
        i % 3 === 0 ? ['header', 'capsule'] : i % 3 === 1 ? ['icon'] : []),
    });
  }
  return games;
}

export const STEAM_CATALOG = [
  ['730', 'Counter-Strike 2'], ['570', 'Dota 2'], ['1091500', 'Cyberpunk 2077'], ['1145350', 'Hades II'],
  ['1145360', 'Hades'], ['1245620', 'ELDEN RING'], ['292030', 'The Witcher 3: Wild Hunt'],
  ['413150', 'Stardew Valley'], ['367520', 'Hollow Knight'], ['1086940', "Baldur's Gate 3"],
  ['271590', 'Grand Theft Auto V'], ['1174180', 'Red Dead Redemption 2'], ['105600', 'Terraria'],
  ['620', 'Portal 2'], ['1172470', 'Apex Legends'], ['578080', 'PUBG: BATTLEGROUNDS'],
  ['252490', 'Rust'], ['892970', 'Valheim'], ['1623730', 'Palworld'], ['2358720', 'Black Myth: Wukong'],
];
