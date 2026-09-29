// Settings view: every SettingsDto field, applied immediately via setSettings with a "Salvo" indicator.
import { store } from '../store.js';
import { icon } from '../components/icons.js';
import { esc, debounce } from '../util.js';
import { toast } from '../components/toast.js';
import { installUpdate } from '../components/banner.js';
import * as A from '../actions.js';

const sw = (key, title, desc = '') =>
  `<label class="set-row"><span class="set-text"><b>${title}</b>${desc ? `<small>${desc}</small>` : ''}</span>` +
  `<input type="checkbox" class="switch" role="switch" data-nav data-set="${key}"></label>`;
const sel = (key, title, desc, options) =>
  `<label class="set-row"><span class="set-text"><b>${title}</b>${desc ? `<small>${desc}</small>` : ''}</span>` +
  `<span class="select-wrap"><select class="select" data-nav data-set="${key}">${options.map(([v, l]) => `<option value="${v}">${esc(l)}</option>`).join('')}</select>${icon('chevronDown')}</span></label>`;
const row = (title, desc, control) =>
  `<div class="set-row"><span class="set-text"><b>${title}</b>${desc ? `<small>${desc}</small>` : ''}</span><span class="set-control">${control}</span></div>`;
const group = (id, title, content) =>
  `<section class="set-group" aria-labelledby="sg-${id}"><h2 class="set-title" id="sg-${id}">${title}</h2><div class="set-card">${content}</div></section>`;

export function initSettings(root) {
  root.innerHTML = `
    <div class="settings">
      <header class="page-head"><h1>Configurações</h1><span class="saved" role="status" aria-live="polite">${icon('check')}Salvo</span></header>
      ${group('lib', 'Biblioteca',
        row('Pasta de jogos', 'Atalhos (.lnk, .url) e executáveis desta pasta entram na biblioteca.',
          '<code class="path" data-show="gamesDir"></code><button type="button" class="btn btn-ghost btn-sm" data-nav data-cmd="pickGamesDir">' + icon('folder') + 'Alterar…</button>') +
        sw('importSteam', 'Importar jogos da Steam', 'Detecta jogos instalados em todas as bibliotecas da Steam.') +
        sw('importEpic', 'Importar jogos da Epic Games', 'Detecta jogos instalados pela Epic Games Launcher.') +
        row('Procurar jogos novamente', 'Relê a pasta e as plataformas agora. Atalho: F5.',
          '<button type="button" class="btn btn-ghost btn-sm" data-nav data-cmd="rescan">' + icon('refresh') + 'Reescanear</button>'))}
      ${group('behavior', 'Comportamento',
        sel('onLaunch', 'Ao iniciar um jogo', '', [['tray', 'Esconder na bandeja'], ['minimize', 'Minimizar a janela'], ['none', 'Não fazer nada']]) +
        sw('closeToTray', 'Fechar para a bandeja', 'O botão fechar mantém o GamesHub rodando na área de notificação.') +
        sw('startWithWindows', 'Iniciar com o Windows') +
        sw('startMinimized', 'Iniciar minimizado', 'Abre direto na bandeja, sem mostrar a janela.') +
        sw('hotkeyEnabled', 'Atalho global', 'Mostra o GamesHub de qualquer lugar.') +
        row('Combinação do atalho', 'Clique e pressione as teclas desejadas.',
          '<input class="input input-sm hotkey" data-nav data-hotkey readonly aria-label="Combinação do atalho global">'))}
      ${group('look', 'Aparência',
        sel('cardStyle', 'Estilo dos cards', 'Paisagem usa o cabeçalho; retrato usa a capa.', [['landscape', 'Paisagem'], ['portrait', 'Retrato']]) +
        sel('view', 'Exibição da biblioteca', '', [['grid', 'Grade'], ['list', 'Lista']]) +
        sw('reduceMotion', 'Reduzir animações', 'Desativa transições e efeitos de movimento.') +
        sel('language', 'Idioma', '', [['pt-BR', 'Português (Brasil)']]))}
      ${group('art', 'Artes',
        sw('autoArtwork', 'Buscar artes automaticamente', 'Baixa capas, fundos e logotipos da Steam.') +
        row('Chave da SteamGridDB', 'Opcional. Melhora as artes de jogos fora da Steam. <a href="#" data-link="https://www.steamgriddb.com/profile/preferences/api">Obter uma chave</a>',
          '<span class="input-group input-group-sm"><input class="input input-sm" type="password" data-nav data-text="steamGridDbKey" placeholder="Cole sua chave" autocomplete="off" spellcheck="false">' +
          `<button type="button" class="icon-btn" data-toggle-secret aria-label="Mostrar chave" title="Mostrar chave">${icon('eye')}</button></span>`))}
      ${group('time', 'Tempo de jogo',
        sw('trackPlaytime', 'Rastrear tempo de jogo', 'Conta o tempo enquanto o processo do jogo está aberto.'))}
      ${group('updates', 'Atualizações',
        sw('checkUpdates', 'Verificar atualizações ao iniciar') +
        row('Repositório', 'Formato <code>dono/repositório</code> no GitHub. Vazio desativa o atualizador.',
          '<input class="input input-sm" type="text" data-nav data-text="updateRepo" placeholder="dono/repositório" spellcheck="false">') +
        row('Verificar agora', '<span data-update-status></span>',
          '<button type="button" class="btn btn-ghost btn-sm" data-nav data-cmd="checkUpdate">' + icon('refresh') + 'Verificar agora</button>' +
          '<button type="button" class="btn btn-primary btn-sm" data-nav data-cmd="installUpdate" hidden>' + icon('download') + 'Instalar</button>') +
        '<div class="set-progress" hidden><div class="progress"><i></i></div><span></span></div>')}
      ${group('about', 'Sobre',
        row('GamesHub', '<span data-show="version"></span>', '') +
        row('Pasta de dados', 'Configurações, cache de artes e biblioteca.',
          '<button type="button" class="btn btn-ghost btn-sm" data-nav data-cmd="openDataFolder">' + icon('folder') + 'Abrir pasta</button>') +
        row('Registros (logs)', 'Útil para relatar problemas: <code>logs\\gameshub.log</code>',
          '<button type="button" class="btn btn-ghost btn-sm" data-nav data-cmd="openLogs">' + icon('file') + 'Abrir logs</button>'))}
    </div>`;

  const q = (s) => root.querySelector(s);
  const savedEl = q('.saved');
  let savedTimer = null;
  const flashSaved = () => {
    savedEl.classList.add('show');
    clearTimeout(savedTimer);
    savedTimer = setTimeout(() => savedEl.classList.remove('show'), 1600);
  };
  const save = async (patch) => { if (await A.saveSettings(patch)) flashSaved(); };

  root.addEventListener('change', (e) => {
    const k = e.target.dataset.set;
    if (!k) return;
    save({ [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value });
  });

  const saveText = debounce((k, v) => save({ [k]: v }), 700);
  root.addEventListener('input', (e) => {
    const k = e.target.dataset.text;
    if (k) saveText(k, e.target.value.trim());
  });
  root.addEventListener('focusout', (e) => { if (e.target.dataset.text) saveText.flush(e.target.dataset.text, e.target.value.trim()); });

  const hk = q('[data-hotkey]');
  hk.addEventListener('keydown', (e) => {
    if (e.key === 'Tab') return;
    e.preventDefault();
    e.stopPropagation();
    if (e.key === 'Escape') { hk.value = store.state.settings.hotkey; hk.blur(); return; }
    const combo = comboFromEvent(e);
    if (combo) { hk.value = combo; save({ hotkey: combo }); }
    else hk.value = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean).join('+') + '+…';
  });
  hk.addEventListener('blur', () => { hk.value = store.state.settings.hotkey; });

  root.addEventListener('click', async (e) => {
    const link = e.target.closest('[data-link]');
    if (link) { e.preventDefault(); A.run('openExternal', { url: link.dataset.link }); return; }
    const secret = e.target.closest('[data-toggle-secret]');
    if (secret) {
      const input = secret.parentElement.querySelector('input');
      const show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      secret.innerHTML = icon(show ? 'eyeOff' : 'eye');
      secret.setAttribute('aria-label', show ? 'Ocultar chave' : 'Mostrar chave');
      return;
    }
    const b = e.target.closest('[data-cmd]');
    if (!b) return;
    switch (b.dataset.cmd) {
      case 'pickGamesDir': {
        const s = await A.run('pickGamesDir');
        if (s && !s.cancelled) { store.set({ settings: { ...store.state.settings, ...s } }); flashSaved(); }
        break;
      }
      case 'rescan': A.rescan(); break;
      case 'openDataFolder': A.run('openDataFolder'); break;
      case 'openLogs': A.run('openDataFolder', { sub: 'logs' }); break;
      case 'installUpdate': installUpdate(); break;
      case 'checkUpdate': {
        const status = q('[data-update-status]');
        b.disabled = true;
        status.textContent = 'Verificando…';
        const info = await A.run('checkUpdate');
        b.disabled = false;
        if (!info) { status.textContent = 'Não foi possível verificar.'; break; }
        if (info.available) {
          status.textContent = `Versão ${info.version} disponível.`;
          store.set({ update: { version: info.version, notes: info.notes, pageUrl: info.pageUrl } });
        } else {
          status.textContent = 'Você já está na versão mais recente.';
          toast('Você já está na versão mais recente.', { kind: 'info' });
        }
        break;
      }
    }
  });

  return function render(changed) {
    const s = store.state;
    if (changed.has('settings') || changed.has('init') || (changed.has('route') && s.route.view === 'settings')) {
      const st = s.settings;
      for (const el of root.querySelectorAll('[data-set]')) {
        if (el === document.activeElement && el.tagName === 'SELECT') continue;
        const v = st[el.dataset.set];
        if (el.type === 'checkbox') el.checked = !!v; else if (v !== undefined) el.value = v;
      }
      for (const el of root.querySelectorAll('[data-text]')) if (el !== document.activeElement) el.value = st[el.dataset.text] ?? '';
      if (hk !== document.activeElement) hk.value = st.hotkey || '';
      hk.disabled = !st.hotkeyEnabled;
      q('[data-show="gamesDir"]').textContent = st.gamesDir || '(não definida)';
      q('[data-show="gamesDir"]').title = st.gamesDir || '';
    }
    if (changed.has('version') || changed.has('demo') || changed.has('init')) {
      q('[data-show="version"]').textContent = `Versão ${s.version || '—'}${s.demo ? ' · modo demonstração' : ''}`;
    }
    if (changed.has('update') || changed.has('updatePercent') || changed.has('init')) {
      q('[data-cmd="installUpdate"]').hidden = !s.update || s.updatePercent != null;
      const prog = q('.set-progress');
      prog.hidden = s.updatePercent == null;
      if (s.updatePercent != null) {
        const pct = Math.round(s.updatePercent);
        prog.querySelector('i').style.width = `${pct}%`;
        prog.querySelector('span').textContent = pct >= 100 ? 'Instalando…' : `Baixando atualização… ${pct}%`;
      }
    }
  };
}

const KEY_NAMES = { ' ': 'Space', ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right' };

/** "Ctrl+Alt+G" style combo, or null when only modifiers are held / no modifier with a plain key. */
export function comboFromEvent(e) {
  if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return null;
  let key = KEY_NAMES[e.key] || e.key;
  if (/^Key[A-Z]$/.test(e.code)) key = e.code.slice(3);
  else if (/^Digit\d$/.test(e.code)) key = e.code.slice(5);
  else if (key.length === 1) key = key.toUpperCase();
  const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean);
  const isF = /^F\d{1,2}$/.test(key);
  if (!mods.length && !isF) return null;
  return [...mods, key].join('+');
}
