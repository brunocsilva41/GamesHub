// Drives the running (isolated) GamesHub through the WebView2 DevTools protocol and saves PNG screenshots.
// Usage: node tools/screenshots.mjs <port> <outDir>   (started by tools/screenshots.ps1)
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [, , port, outDir] = process.argv;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function targets() {
  for (let i = 0; i < 60; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
      if (list.some((p) => p.url.endsWith('/index.html') && !p.url.includes('/quick/'))) return list;
    } catch { /* not up yet */ }
    await sleep(500);
  }
  throw new Error('WebView2 DevTools endpoint did not come up');
}

function connect(target) {
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  let id = 0;
  const pending = new Map();
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  };
  const send = (method, params = {}) => new Promise((resolve) => {
    const i = ++id; pending.set(i, resolve); ws.send(JSON.stringify({ id: i, method, params }));
  });
  const ready = new Promise((r) => { ws.onopen = r; });
  const evaluate = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 300));
    return r.result?.result?.value;
  };
  const shot = async (file) => {
    const r = await send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(join(outDir, file), Buffer.from(r.result.data, 'base64'));
    console.log('saved', file);
  };
  return { ready, send, evaluate, shot, close: () => ws.close() };
}

const list = await targets();
const main = connect(list.find((p) => p.url.endsWith('/index.html') && !p.url.includes('/quick/')));
await main.ready;

// Wait until the library is rendered and artwork had time to download.
const STORE = "(await import('/js/store.js'))";
for (let i = 0; i < 80; i++) {
  const n = await main.evaluate(`(async () => { const m = ${STORE}; const s = m.store || m.default; return (s.state.games || []).filter(g => g.art && g.art.header).length; })()`);
  if (n >= 14) break;
  await sleep(750);
}
await sleep(2500);
await main.evaluate('document.scrollingElement && (document.scrollingElement.scrollTop = 0), true');
await main.shot('biblioteca.png');

const A = "(await import('/js/actions.js'))";
await main.evaluate(`(async () => { const m = ${STORE}; const s = m.store || m.default;
  const g = s.state.games.find(x => x.name === 'ELDEN RING') || s.state.games[0]; ${A}.openDetails(g.id); return true; })()`);
await sleep(3500);
await main.shot('detalhes.png');

// Big Picture view rendered inside the window (no OS fullscreen, so the desktop is never covered).
await main.evaluate(`(async () => { const m = ${STORE}; const s = m.store || m.default; ${A}.openLibrary?.();
  s.set({ route: { view: 'library' }, bigPicture: true }); return true; })()`);
await sleep(2500);
await main.shot('big-picture.png');
await main.evaluate(`(async () => { const m = ${STORE}; (m.store || m.default).set({ bigPicture: false }); return true; })()`);

// Quick-launch palette: its page lives in a separate (hidden) window; capture its surface directly.
const quickTarget = (await targets()).find((p) => p.url.includes('/quick/index.html'));
if (quickTarget) {
  const quick = connect(quickTarget);
  await quick.ready;
  await quick.send('Emulation.setDeviceMetricsOverride', { width: 640, height: 420, deviceScaleFactor: 1, mobile: false });
  await quick.evaluate(`(() => { const i = document.querySelector('input'); if (i) { i.value = 'ho'; i.dispatchEvent(new Event('input', { bubbles: true })); } return true; })()`);
  await sleep(1200);
  await quick.shot('busca-rapida.png');
  quick.close();
} else {
  console.warn('quick-launch target not found; busca-rapida.png not regenerated');
}
main.close();
