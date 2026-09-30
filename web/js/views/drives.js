// Settings › Discos: one expandable item per drive with the games installed on it (largest first), how much
// each takes (GB and % of the drive) and a bar split into games / other files / free space.
import { esc, fmtBytes } from '../util.js';
import { icon } from '../components/icons.js';

const pct = (part, total) => (total > 0 ? (part / total) * 100 : 0);
/** "1,3%", "< 0,1%" (pt-BR). */
export function fmtPct(value) {
  if (value > 0 && value < 0.1) return '< 0,1%';
  return `${value.toLocaleString('pt-BR', { maximumFractionDigits: value < 10 ? 1 : 0 })}%`;
}

/** Groups games by drive root; sizes of -1 (still being measured) count as unknown. Pure; exported for tests. */
export function groupByDrive(drives, games) {
  const byRoot = new Map((drives || []).map((d) => [String(d.name).toUpperCase(), []]));
  const unknown = [];
  for (const g of games || []) {
    const list = byRoot.get(String(g.drive || '').toUpperCase());
    (list || unknown).push(g);
  }
  const bySize = (a, b) => (Number(b.sizeBytes) || -1) - (Number(a.sizeBytes) || -1) || String(a.name).localeCompare(String(b.name), 'pt-BR');
  for (const list of byRoot.values()) list.sort(bySize);
  unknown.sort((a, b) => String(a.name).localeCompare(String(b.name), 'pt-BR'));
  return { byRoot, unknown };
}

/** "known" | "measuring" | "unavailable" (older backends only send sizeBytes; -1 then means measuring). */
const sizeState = (g) => g.sizeState || (Number(g.sizeBytes) >= 0 ? 'known' : 'measuring');

/**
 * Disk usage of a drive's games, counting each install folder once (a game and its mod launcher share one
 * folder). Returns { bytes, measuring, sharedWith: Map(gameId -> name of the game already counted) }.
 */
export function driveTotals(games) {
  const counted = new Map();   // folder -> game
  const sharedWith = new Map();
  let bytes = 0, measuring = false;
  for (const g of games) {
    const st = sizeState(g);
    if (st === 'measuring') measuring = true;
    // Loose files without an install of their own (e.g. .exe in the games folder) never share a folder.
    const folder = st === 'unavailable' ? '' : String(g.folder || '').toLowerCase();
    if (folder && counted.has(folder)) { sharedWith.set(g.id, counted.get(folder).name); continue; }
    if (folder) counted.set(folder, g);
    if (st === 'known') bytes += Number(g.sizeBytes);
  }
  return { bytes, measuring, sharedWith };
}

function gameRow(g, total, sharedWith) {
  const size = Number(g.sizeBytes);
  const st = sizeState(g);
  const shared = sharedWith.get(g.id);
  const known = st === 'known' && !shared;
  const share = known ? pct(size, total) : 0;
  const sizeText = shared ? `<span class="muted" title="Os arquivos ficam na mesma pasta; já contados em ${esc(shared)}">mesma pasta de ${esc(shared)}</span>`
    : known ? esc(fmtBytes(size))
    : st === 'unavailable' ? '<span class="muted" title="Atalho ou arquivo solto, sem pasta de instalação própria para medir">tamanho indisponível</span>'
    : '<span class="muted">calculando…</span>';
  return `<li class="dg-row${shared ? ' is-shared' : ''}">
    <button type="button" class="dg-name" data-nav data-open-game="${esc(g.id)}" title="Abrir detalhes de ${esc(g.name)}">${esc(g.name)}</button>
    <span class="dg-plat">${esc(g.platform || '')}</span>
    <span class="dg-size">${sizeText}</span>
    <span class="dg-pct">${known ? esc(fmtPct(share)) : ''}</span>
    <span class="dg-bar" aria-hidden="true"><i style="width:${Math.min(100, share)}%"></i></span>
  </li>`;
}

/** Markup for getDrives { drives, games } (pure; exported for tests). */
export function drivesHtml(data) {
  const drives = data?.drives || [];
  if (!drives.length) return '<p class="muted">Nenhuma unidade encontrada.</p>';
  const { byRoot, unknown } = groupByDrive(drives, data.games);
  const items = drives.map((d) => {
    const total = Number(d.totalBytes) || 0;
    const free = Math.max(0, Number(d.freeBytes) || 0);
    const used = Math.max(0, total - free);
    const games = byRoot.get(String(d.name).toUpperCase()) || [];
    const { bytes: gamesBytes, measuring, sharedWith } = driveTotals(games);
    const low = total > 0 && free / total < 0.1;
    const name = `${d.name}${d.label ? ` ${d.label}` : ''}`;
    const gShare = Math.min(100, pct(gamesBytes, total));
    const oShare = Math.max(0, Math.min(100 - gShare, pct(used - gamesBytes, total)));
    const count = `${games.length} ${games.length === 1 ? 'jogo' : 'jogos'}`;
    const summary = !games.length ? 'Nenhum jogo nesta unidade'
      : gamesBytes > 0 ? `${count} · ${fmtBytes(gamesBytes)} (${fmtPct(pct(gamesBytes, total))} do disco)${measuring ? ' · medindo…' : ''}`
      : measuring ? `${count} · medindo tamanhos…`
      : `${count} · tamanho indisponível`;
    return `<details class="drive${low ? ' is-low' : ''}"${games.length ? '' : ' data-empty'}>
      <summary class="drive-sum" data-nav>
        <span class="drive-head">${icon('chevronRight')}${icon('drive')}<b>${esc(name)}</b>
          <span>${esc(fmtBytes(free))} livres de ${esc(fmtBytes(total))}${low ? ' · pouco espaço' : ''}</span></span>
        <span class="progress drive-bar drive-bar-split" role="meter" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${Math.round(pct(used, total))}"
          aria-label="${esc(name)}: ${esc(fmtPct(gShare))} em jogos, ${esc(fmtPct(oShare))} em outros arquivos, ${esc(fmtPct(pct(free, total)))} livre">
          <i class="seg-games" style="width:${gShare}%"></i><i class="seg-other" style="width:${oShare}%"></i></span>
        <span class="drive-note">${esc(summary)}</span>
      </summary>
      ${games.length ? `<ul class="dg-list">${games.map((g) => gameRow(g, total, sharedWith)).join('')}</ul>` : ''}
    </details>`;
  }).join('');
  const legend = '<p class="drive-legend"><i class="seg-games"></i>Jogos <i class="seg-other"></i>Outros arquivos <i class="seg-free"></i>Livre</p>';
  const rest = unknown.length
    ? `<details class="drive drive-unknown"><summary class="drive-sum" data-nav><span class="drive-head">${icon('chevronRight')}${icon('help')}<b>Local desconhecido</b>
        <span>${unknown.length} ${unknown.length === 1 ? 'jogo' : 'jogos'}</span></span>
        <span class="drive-note">Atalhos e jogos abertos por launcher sem pasta de instalação conhecida.</span></summary>
        <ul class="dg-list">${unknown.map((g) => `<li class="dg-row"><button type="button" class="dg-name" data-nav data-open-game="${esc(g.id)}">${esc(g.name)}</button><span class="dg-plat">${esc(g.platform || '')}</span></li>`).join('')}</ul></details>`
    : '';
  return legend + items + rest;
}
