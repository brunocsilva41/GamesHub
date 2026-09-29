// Empty / no-results / error state markup.
import { icon } from '../components/icons.js';
import { esc, platformColor } from '../util.js';
import { sectionLabel, QUICK_FILTERS } from '../store.js';

const block = (ic, title, text, actions = '') =>
  `<div class="state"><div class="state-ic">${ic}</div><h2 class="state-title">${title}</h2>` +
  `<p class="state-text">${text}</p>${actions ? `<div class="state-actions">${actions}</div>` : ''}</div>`;
const btn = (act, label, primary = false, ic = '') =>
  `<button type="button" class="btn ${primary ? 'btn-primary' : 'btn-ghost'}" data-nav data-state-action="${act}">${ic ? icon(ic) : ''}${label}</button>`;

/** Returns HTML for the library state panel, or '' when the grid has content. */
export function renderLibraryState(s, visibleCount) {
  if (s.games.length === 0) {
    const dir = s.settings.gamesDir ? `<code>${esc(s.settings.gamesDir)}</code>` : 'de jogos';
    return block(icon('gamepad'), 'Sua biblioteca está vazia',
      `Coloque atalhos (<b>.lnk</b>, <b>.url</b>) ou executáveis (<b>.exe</b>) na pasta ${dir}, ` +
      'ou arraste-os para esta janela. Jogos instalados pela Steam e pela Epic aparecem automaticamente.',
      btn('add', 'Adicionar jogo', true, 'plus') + btn('settings', 'Configurações', false, 'settings') + btn('rescan', 'Procurar novamente', false, 'refresh'));
  }
  if (visibleCount > 0) return '';
  if (s.genre && !s.query.trim()) {
    return block(icon('tag'), 'Nenhum jogo deste gênero aqui',
      `Nenhum jogo de “${esc(s.genre)}” em ${esc(sectionLabel(s.section))}.`, btn('genre-clear', 'Todos os gêneros', true));
  }
  if (s.quick && !s.query.trim()) {
    const f = QUICK_FILTERS.find((x) => x.key === s.quick);
    return block(icon('clock'), 'Nenhum jogo com este filtro',
      `Nenhum jogo em “${esc(f?.label || s.quick)}” em ${esc(sectionLabel(s.section))}.`, btn('quick-clear', 'Limpar filtro', true));
  }
  if (s.query.trim()) {
    return block(icon('search'), 'Nenhum resultado',
      `Nada encontrado para “${esc(s.query.trim())}” em ${esc(sectionLabel(s.section))}. Tente outro nome, plataforma ou coleção.`,
      btn('clear', 'Limpar busca', true) + (s.section !== 'all' ? btn('all', 'Buscar em Todos') : ''));
  }
  switch (s.section) {
    case 'favorites':
      return block(icon('star'), 'Nenhum favorito ainda', 'Clique na estrela de um jogo ou pressione <kbd>F</kbd> com ele selecionado.', btn('all', 'Ver todos os jogos', true));
    case 'recent':
      return block(icon('clock'), 'Nada jogado recentemente', 'Os jogos que você abrir pelo GamesHub aparecem aqui.', btn('all', 'Ver todos os jogos', true));
    case 'hidden':
      return block(icon('eyeOff'), 'Nenhum jogo oculto', 'Jogos ocultos ficam fora da biblioteca, mas continuam instalados.', btn('all', 'Ver todos os jogos', true));
    default: {
      const dot = s.section.startsWith('platform:') ? `<span class="nav-dot" style="--dot:${platformColor(s.section.slice(9))}"><i></i></span>` : icon('tag');
      return block(dot, 'Nada por aqui', `Nenhum jogo em ${esc(sectionLabel(s.section))}.`, btn('all', 'Ver todos os jogos', true));
    }
  }
}

export function renderErrorState(message) {
  return block(icon('alert'), 'Não foi possível carregar a biblioteca',
    `${esc(message || 'O aplicativo não respondeu.')} Tente novamente; se o problema continuar, reinicie o GamesHub.`,
    '<button type="button" class="btn btn-primary" data-nav data-retry>' + icon('refresh') + 'Tentar novamente</button>');
}
