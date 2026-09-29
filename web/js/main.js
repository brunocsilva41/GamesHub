// Entry point: connects the bridge, wires C# events into the store, mounts views and input handlers.
import { connect } from './bridge.js';
import { store } from './store.js';
import { $ } from './util.js';
import * as A from './actions.js';
import { initToasts, toast } from './components/toast.js';
import { initModals } from './components/modal.js';
import { initMenus } from './components/contextmenu.js';
import { initTitlebar } from './components/titlebar.js';
import { initSidebar } from './components/sidebar.js';
import { initBanner } from './components/banner.js';
import { initDropOverlay } from './components/dropzone.js';
import { initLibrary } from './views/library.js';
import { initDetails } from './views/details.js';
import { initSettings } from './views/settings.js';
import { initBigPicture } from './views/bigpicture.js';
import { renderErrorState } from './views/states.js';
import { initKeyboard } from './input/keyboard.js';
import { initGamepad } from './input/gamepad.js';

const renderers = [];
let pending = null;

function schedule(changed) {
  if (!pending) {
    pending = new Set();
    queueMicrotask(() => {
      const keys = pending;
      pending = null;
      for (const r of renderers) {
        try { r(keys); } catch (err) { reportError(err); }
      }
    });
  }
  for (const k of changed) pending.add(k);
}

function reportError(err) {
  console.error(err);
  A.getBridge()?.log('error', `[ui] ${err?.stack || err}`);
}

const reducedMotionQuery = matchMedia('(prefers-reduced-motion: reduce)');

function shellRenderer(changed) {
  const s = store.state;
  const body = document.body;
  if (changed.has('route') || changed.has('status') || changed.has('init')) {
    const err = s.status === 'error';
    $('#app-error').hidden = !err;
    $('#view-library').hidden = err || s.route.view !== 'library';
    $('#view-game').hidden = err || s.route.view !== 'game';
    $('#view-settings').hidden = err || s.route.view !== 'settings';
    if (err) $('#app-error').innerHTML = renderErrorState(s.error);
    if (changed.has('route') && s.route.view === 'settings') $('#view-settings').scrollTop = 0;
  }
  if (changed.has('settings') || changed.has('init')) {
    body.classList.toggle('reduce-motion', !!s.settings.reduceMotion || reducedMotionQuery.matches);
  }
  if (changed.has('padActive') || changed.has('init')) body.classList.toggle('pad-active', !!s.padActive);
  if (changed.has('windowState') || changed.has('init')) {
    body.classList.toggle('is-maximized', !!s.windowState.maximized || !!s.windowState.fullscreen);
  }
  if (changed.has('sidebarCollapsed') || changed.has('init')) {
    try { localStorage.setItem('gh.sidebarCollapsed', s.sidebarCollapsed ? '1' : '0'); } catch { /* storage unavailable */ }
  }
}

function wireEvents(bridge) {
  bridge.on('games', (d) => store.set({ games: d.games || [], collections: d.collections || store.state.collections }));
  bridge.on('running', (d) => store.patchGame(d.id, { running: !!d.running }));
  bridge.on('settings', (d) => store.set({ settings: { ...store.state.settings, ...d } }));
  bridge.on('toast', (d) => toast(d.text, { kind: d.kind || 'info', undoToken: d.undoToken }));
  bridge.on('update', (d) => store.set({ update: d }));
  bridge.on('updateProgress', (d) => store.set({ updatePercent: Number(d.percent) || 0 }));
  bridge.on('windowState', (d) => store.set({ windowState: { ...store.state.windowState, ...d } }));
  bridge.on('focusSearch', () => A.focusSearch());
  bridge.on('navigate', (d) => {
    if (store.state.bigPicture && d.view !== 'library') A.toggleBigPicture(false);
    if (d.view === 'settings') A.openSettings();
    else if (d.view === 'game' && d.id) A.openDetails(d.id);
    else A.goLibrary();
  });
}

async function loadState() {
  store.set({ status: 'loading', error: null });
  try {
    const st = await A.getBridge().call('getState', {}, { timeout: 20000 });
    store.set({
      games: st.games || [], collections: st.collections || [],
      settings: { ...store.state.settings, ...(st.settings || {}) },
      version: st.version || '', windowState: { ...store.state.windowState, ...(st.windowState || {}) },
      demo: !!st.demo || A.getBridge().isMock,
      status: 'ready',
    });
  } catch (err) {
    store.set({ status: 'error', error: err.message });
    A.getBridge()?.log('error', `getState failed: ${err.message}`);
  }
}

async function boot() {
  initToasts($('#toasts'), (token) => A.undo(token));
  initModals($('#modal-root'));
  initMenus($('#menu-root'));
  try { store.set({ sidebarCollapsed: localStorage.getItem('gh.sidebarCollapsed') === '1' }); } catch { /* storage unavailable */ }

  renderers.push(
    shellRenderer,
    initTitlebar($('#titlebar')),
    initSidebar($('#sidebar')),
    initBanner($('#update-banner')),
    initLibrary($('#view-library')),
    initDetails($('#view-game')),
    initSettings($('#view-settings')),
    initBigPicture($('#bigpicture')),
  );
  store.on('change', schedule);
  reducedMotionQuery.addEventListener?.('change', () => schedule(['settings']));
  schedule(['init']);

  initDropOverlay($('#drop-overlay'));
  initKeyboard(() => { if (store.state.padActive) store.set({ padActive: false }); });
  initGamepad();

  $('#app-error').addEventListener('click', (e) => { if (e.target.closest('[data-retry]')) loadState(); });

  let bridge;
  try {
    bridge = await connect();
  } catch (err) {
    store.set({ status: 'error', error: 'Não foi possível conectar à interface do aplicativo.' });
    console.error(err);
    return;
  }
  A.setBridge(bridge);
  store.set({ demo: bridge.isMock });
  window.addEventListener('error', (e) => bridge.log('error', `${e.message} @ ${e.filename}:${e.lineno}`));
  window.addEventListener('unhandledrejection', (e) => bridge.log('error', `unhandled: ${e.reason?.stack || e.reason}`));
  wireEvents(bridge);
  await loadState();
}

boot();
