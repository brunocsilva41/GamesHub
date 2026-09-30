// Settings additions: hotkey editors (<gh-hotkey-input>), sources & data toggles,
// default automation profile, disk usage ("Discos").
import '../../quick/hotkey-input.js';
import { store } from '../store.js';
import { icon } from '../components/icons.js';
import { sw, row, group } from './setui.js';
import { createAutomationEditor } from './automation.js';
import * as A from '../actions.js';
import { drivesHtml } from './drives.js';

const hk = (key, label) =>
  `<gh-hotkey-input data-nav data-hk="${key}" label="${label}" allow-empty="false"></gh-hotkey-input>`;

export const hotkeysGroupHtml = () => group('keys', 'Atalhos de teclado',
  sw('hotkeyEnabled', 'Atalho para abrir o GamesHub', 'Mostra a janela de qualquer lugar do Windows.') +
  row('Combinação', 'Clique e pressione as teclas. Esc cancela.', hk('hotkey', 'Atalho para abrir o GamesHub')) +
  sw('quickLaunchEnabled', 'Busca rápida', 'Uma paleta flutuante para abrir jogos sem sair do que você está fazendo.') +
  row('Combinação da busca rápida', 'Funciona mesmo com o GamesHub na bandeja.', hk('quickLaunchHotkey', 'Atalho da busca rápida')));

export const sourcesGroupHtml = () => group('sources', 'Fontes e dados',
  sw('importRiot', 'Importar jogos da Riot Games', 'VALORANT, League of Legends e outros instalados pelo Riot Client.') +
  sw('importHydra', 'Importar jogos do Hydra Launcher', 'Jogos instalados pelo Hydra aparecem na biblioteca.') +
  sw('importSteamPlaytime', 'Importar tempo de jogo da Steam', 'Usa o tempo e a última sessão registrados pela Steam neste PC.') +
  sw('fetchMetadata', 'Buscar informações dos jogos', 'Gêneros, descrição e lançamento vindos da loja da Steam.') +
  sw('pcgwEnabled', 'Consultar o PCGamingWiki', 'Links de correções e locais de saves e configurações.') +
  sw('minimizeLaunchers', 'Abrir launchers minimizados', 'Steam inicia só na bandeja e janelas da Steam, Epic, Riot e outros launchers são minimizadas ao abrir um jogo.'));

export const automationGroupHtml = () => group('auto', 'Automação padrão',
  sw('automationEnabled', 'Automação antes e depois de jogar', 'Abre ou fecha programas e ajusta energia, áudio e resolução ao jogar.') +
  '<div class="set-block"><p class="set-desc">Executada para todos os jogos (cada jogo pode desligá-la em Detalhes › Antes e depois de jogar).</p><div data-auto-default></div></div>');

export const drivesGroupHtml = () => group('drives', 'Discos',
  row('Espaço em disco', 'Quanto cada unidade tem livre e quais jogos estão nela. Clique numa unidade para ver os jogos.',
    `<button type="button" class="btn btn-ghost btn-sm" data-nav data-cmd="drives">${icon('refresh')}Atualizar</button>`) +
  '<div class="drives" data-drives aria-live="polite"></div>');

export { drivesHtml } from './drives.js';

export function initSettingsExtra(root, flashSaved) {
  const editor = createAutomationEditor(root.querySelector('[data-auto-default]'), { perGame: false });
  const drivesEl = root.querySelector('[data-drives]');
  let loaded = false;

  async function loadDrives() {
    drivesEl.innerHTML = '<p class="muted"><span class="spinner" aria-hidden="true"></span>Verificando discos…</p>';
    // Keep expanded drives open across refreshes.
    const open = new Set(Array.from(drivesEl.querySelectorAll('details[open] b')).map((b) => b.textContent));
    const d = await A.run('getDrives');
    drivesEl.innerHTML = d ? drivesHtml(d) : '<p class="muted err">Não foi possível ler os discos.</p>';
    for (const det of drivesEl.querySelectorAll('details')) if (open.has(det.querySelector('b')?.textContent)) det.open = true;
  }

  // Hotkeys: save on change; a rejected combo reverts and shows the backend message under the field.
  // Registration failures ("em uso por outro programa") arrive later as an error toast event.
  root.addEventListener('change', async (e) => {
    const el = e.target.closest?.('gh-hotkey-input');
    if (!el || !e.detail) return;
    const key = el.dataset.hk;
    const value = e.detail.value;
    if (!value) { el.value = store.state.settings[key] || ''; return; }
    const bridge = A.getBridge();
    const off = bridge?.on('toast', (t) => { if (t.kind === 'err' && /atalho|tecla|combina/i.test(t.text || '')) el.setError(t.text); });
    setTimeout(() => off?.(), 3000);
    const ok = await A.saveSettings({ [key]: value }, { onError: (msg) => { el.value = store.state.settings[key] || ''; el.setError(msg); } });
    if (ok) flashSaved();
  });
  // Gamepad "A" clicks the host element: forward it to the inner button to start recording.
  root.addEventListener('click', (e) => {
    const host = e.target.closest?.('gh-hotkey-input');
    if (host && e.composedPath()[0] === host) host.shadowRoot?.querySelector('button')?.click();
    if (e.target.closest('[data-cmd="drives"]')) loadDrives();
    const game = e.target.closest('[data-open-game]');
    if (game) A.openDetails(game.dataset.openGame);
  });

  return function render(changed) {
    const s = store.state;
    if (changed.has('settings') || changed.has('init') || changed.has('route')) {
      for (const el of root.querySelectorAll('gh-hotkey-input')) {
        const v = s.settings[el.dataset.hk] || '';
        if (el.value !== v && !el.shadowRoot?.querySelector('button.recording')) el.value = v;
        el.toggleAttribute('disabled', !(el.dataset.hk === 'hotkey' ? s.settings.hotkeyEnabled : s.settings.quickLaunchEnabled));
      }
      editor.refresh();
    }
    if ((changed.has('route') || changed.has('status')) && s.route.view === 'settings' && s.status === 'ready') {
      loadDrives();
      if (!loaded || !editor.isDirty()) { loaded = true; editor.load(null); }
    }
  };
}
