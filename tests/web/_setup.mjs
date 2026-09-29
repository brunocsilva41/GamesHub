// Shared helpers for the web-UI tests (node:test, zero dependencies). Not a test file itself
// (the runner only picks up *.test.mjs).
//
// The UI modules are plain browser ES modules. The pure ones (store, util, emitter, bridge, mock*,
// automation-model) import cleanly in Node; for modules that touch the DOM at import time we provide
// only the tiny stubs they need (installDomStubs) instead of a fake DOM.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { withQuietModuleWarnings } from '../../ci/web/lib/contract.mjs';

export const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const WEB = path.join(ROOT, 'web');
export const readRepo = (rel) => readFileSync(path.join(ROOT, rel), 'utf8');

/** Imports web/<rel> (e.g. 'js/store.js'). Same URL => same module instance across a test file. */
export function importWeb(rel) {
  return withQuietModuleWarnings(() => import(pathToFileURL(path.join(WEB, rel)).href));
}

/**
 * The dev-mock schedules long demo timers (a launched game "closes" after 12 s, an update toast after 6 s,
 * an install-progress interval). Unref those so a test file exits as soon as its tests finish;
 * short timers (reply latency) stay ref'd so awaited replies keep the loop alive.
 */
export function unrefLongTimers(minMs = 1000) {
  const { setTimeout: st, setInterval: si } = globalThis;
  globalThis.setTimeout = (fn, ms, ...a) => { const t = st(fn, ms, ...a); if (ms >= minMs) t?.unref?.(); return t; };
  globalThis.setInterval = (fn, ms, ...a) => { const t = si(fn, ms, ...a); t?.unref?.(); return t; };
  return () => { globalThis.setTimeout = st; globalThis.setInterval = si; };
}

/** Replaces console methods with recorders for the duration of `fn`. */
export async function quietConsole(fn, methods = ['log', 'info', 'warn', 'error', 'debug']) {
  const saved = {};
  const calls = [];
  for (const m of methods) { saved[m] = console[m]; console[m] = (...a) => calls.push([m, ...a]); }
  try { return { result: await fn(), calls }; } finally { Object.assign(console, saved); }
}

/**
 * Fake `chrome.webview`: records posted messages; the test answers with reply()/replyRaw() and pushes
 * events with emit(). Mirrors WebView2's { data } MessageEvent shape.
 */
export function fakeHost({ withFiles = true } = {}) {
  const listeners = new Set();
  const host = {
    sent: [],
    files: [],
    throwOnPost: null,
    addEventListener(type, fn) { if (type === 'message') listeners.add(fn); },
    removeEventListener(type, fn) { listeners.delete(fn); },
    postMessage(msg) {
      if (host.throwOnPost) throw host.throwOnPost;
      host.sent.push(JSON.parse(JSON.stringify(msg)));
    },
    deliver(data) { for (const fn of [...listeners]) fn({ data }); },
    reply(id, ok, dataOrError) {
      host.deliver(ok ? { type: 'reply', id, ok: true, data: dataOrError } : { type: 'reply', id, ok: false, error: dataOrError });
    },
    emit(name, data) { host.deliver({ type: 'event', name, data }); },
    last() { return host.sent[host.sent.length - 1]; },
  };
  if (withFiles) {
    host.postMessageWithAdditionalObjects = (msg, files) => { host.files.push([...files]); host.postMessage(msg); };
  }
  return host;
}

/** Installs `globalThis.chrome.webview = host` (bridge.connect() then uses it). Returns an uninstall fn. */
export function installWebview(host) {
  const had = Object.prototype.hasOwnProperty.call(globalThis, 'chrome');
  const prev = globalThis.chrome;
  globalThis.chrome = { webview: host };
  return () => { if (had) globalThis.chrome = prev; else delete globalThis.chrome; };
}

/** Minimal globals for modules that define custom elements / touch `document` at import time. */
export function installDomStubs() {
  if (!globalThis.HTMLElement) globalThis.HTMLElement = class HTMLElement {};
  if (!globalThis.customElements) {
    const reg = new Map();
    globalThis.customElements = { get: (n) => reg.get(n), define: (n, c) => reg.set(n, c) };
  }
}

const DAY = 86400000;
/** GameDto builder with contract defaults (see BridgeDto.Game). */
export function game(over = {}) {
  const id = over.id || `folder:${(over.name || 'jogo').toLowerCase()}.lnk`;
  return {
    id, name: 'Jogo', platform: 'PC', source: 'folder', ext: '.lnk', filePath: '', installDir: '', launchArgs: '',
    steamAppId: '', favorite: false, hidden: false, collections: [], lastPlayed: null, playSeconds: 0,
    addedAt: '2024-01-01T00:00:00.000Z', running: false, sizeBytes: -1, updatePending: false, broken: false,
    brokenReason: '', genres: [], variants: [], art: { header: null, capsule: null, hero: null, logo: null, icon: null },
    ...over,
  };
}
export const daysAgo = (n, now = Date.now()) => new Date(now - n * DAY).toISOString();

/** `["key"] = ...` keys inside the C# method whose signature matches `signature` (brace-matched body). */
export function csDictKeys(src, signature) {
  const at = src.search(signature);
  if (at < 0) throw new Error(`signature not found: ${signature}`);
  let i = src.indexOf('{', at);
  let depth = 0;
  const start = i;
  for (; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}' && --depth === 0) break;
  }
  const body = src.slice(start, i);
  return [...body.matchAll(/\["(\w+)"\]\s*=/g)].map((m) => m[1]);
}

export const camel = (s) => s[0].toLowerCase() + s.slice(1);

/** camelCase names of the public instance fields of C# `class <name>` (what BridgeCommands.Camel() serialises). */
export function csFields(src, className) {
  const at = src.search(new RegExp(`\\bclass\\s+${className}\\b`));
  if (at < 0) throw new Error(`class ${className} not found`);
  const open = src.indexOf('{', at);
  let depth = 0, i = open;
  for (; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}' && --depth === 0) break;
  }
  const body = src.slice(open + 1, i).replace(/\/\/[^\n]*/g, '');
  return [...body.matchAll(/public\s+(?!static|const|sealed|class)[\w<>?,\[\] ]+?\s+(\w+)\s*[=;]/g)].map((m) => camel(m[1]));
}

export const sortedKeys = (o) => Object.keys(o).sort();

/** t.mock.timers.enable across Node 20.4+ (array form) and 20.11+/22+ (options object form). */
export function mockTimers(t, apis = ['setTimeout']) {
  try { t.mock.timers.enable({ apis }); } catch { t.mock.timers.enable(apis); }
}
