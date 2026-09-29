// Shared Steam search results renderer (`searchSteam` command). Results are buttons with data-appid/data-name.
import { getBridge } from '../actions.js';
import { esc } from '../util.js';
import { icon } from '../components/icons.js';

export async function searchSteamInto(container, query, { trailing = '' } = {}) {
  const q = String(query || '').trim();
  const token = (container._token = (container._token || 0) + 1);
  if (q.length < 2 && !/^\d+$/.test(q)) {
    container.innerHTML = '<p class="muted">Digite pelo menos 2 letras ou um App ID.</p>';
    return;
  }
  container.setAttribute('aria-busy', 'true');
  container.innerHTML = '<p class="muted"><span class="spinner" aria-hidden="true"></span>Buscando na Steam…</p>';
  let results;
  try {
    results = (await getBridge().call('searchSteam', { query: q })).results || [];
  } catch (err) {
    if (container._token !== token) return;
    container.removeAttribute('aria-busy');
    container.innerHTML = `<p class="muted err">${esc(err.message || 'Falha na busca.')}</p>`;
    return;
  }
  if (container._token !== token) return;
  container.removeAttribute('aria-busy');
  if (!results.length) {
    container.innerHTML = `<p class="muted">Nenhum jogo encontrado para “${esc(q)}”.</p>`;
    return;
  }
  container.innerHTML = results.map((r) => `
    <button type="button" class="steam-result" role="option" aria-selected="false" data-nav data-appid="${esc(r.appId)}" data-name="${esc(r.name)}">
      <span class="steam-ic">${r.iconUrl ? `<img src="${esc(r.iconUrl)}" alt="" loading="lazy" onerror="this.remove()">` : ''}${icon('steam')}</span>
      <span class="steam-name">${esc(r.name)}</span>
      <span class="steam-id">#${esc(r.appId)}</span>
      ${trailing}
    </button>`).join('');
}
