// JS side of the Bridge protocol (docs/ARCHITECTURE.md): request/reply with incrementing ids,
// promise + timeout, and an event emitter for C# -> JS events. Falls back to mock.js in a browser.
import { Emitter } from './emitter.js';

const DEFAULT_TIMEOUT = 15000;
// 0 = no timeout (native dialogs wait for the user).
const TIMEOUTS = {
  pickFile: 0, pickGamesDir: 0, setArt: 0, installUpdate: 0,
  addFiles: 60000, addSteam: 30000, searchSteam: 20000, refreshArt: 30000, rescan: 60000, checkUpdate: 30000,
};

export class BridgeError extends Error {
  constructor(message, code = 'failed', data = null) {
    super(message);
    this.name = 'BridgeError';
    this.code = code; // 'failed' | 'timeout' | 'transport'
    this.data = data;
  }
}

class Bridge extends Emitter {
  constructor(host, isMock) {
    super();
    this.host = host;
    this.isMock = isMock;
    this._seq = 0;
    this._pending = new Map();
    host.addEventListener('message', (e) => this._receive(e.data));
  }

  _receive(msg) {
    if (typeof msg === 'string') {
      try { msg = JSON.parse(msg); } catch { console.warn('[bridge] invalid message', msg); return; }
    }
    if (!msg || typeof msg !== 'object') return;
    if (msg.type === 'reply') {
      const p = this._pending.get(msg.id);
      if (!p) return;
      this._pending.delete(msg.id);
      clearTimeout(p.timer);
      if (msg.ok) p.resolve(msg.data ?? {});
      else p.reject(new BridgeError(msg.error || 'Algo deu errado.', 'failed', msg.data ?? null));
    } else if (msg.type === 'event' && msg.name) {
      this.emit(msg.name, msg.data ?? {});
    }
  }

  /**
   * Sends a command. Resolves with `data` when ok, rejects with BridgeError otherwise.
   * opts.files: File[] sent as WebView2 additional objects (dropped files).
   */
  call(name, args = {}, opts = {}) {
    const id = ++this._seq;
    const msg = { type: 'cmd', id, name, args: args || {} };
    const timeout = opts.timeout ?? TIMEOUTS[name] ?? DEFAULT_TIMEOUT;
    return new Promise((resolve, reject) => {
      const entry = { resolve, reject, timer: null };
      if (timeout > 0) {
        entry.timer = setTimeout(() => {
          this._pending.delete(id);
          reject(new BridgeError('O aplicativo não respondeu a tempo.', 'timeout'));
        }, timeout);
      }
      this._pending.set(id, entry);
      try {
        const files = opts.files ? Array.from(opts.files) : [];
        if (files.length) {
          if (typeof this.host.postMessageWithAdditionalObjects !== 'function') {
            throw new Error('postMessageWithAdditionalObjects indisponível');
          }
          this.host.postMessageWithAdditionalObjects(msg, files);
        } else {
          this.host.postMessage(msg);
        }
      } catch (err) {
        this._pending.delete(id);
        clearTimeout(entry.timer);
        reject(new BridgeError('Falha de comunicação com o aplicativo.', 'transport', String(err)));
      }
    });
  }

  log(level, msg) {
    this.call('log', { level, msg: String(msg).slice(0, 2000) }).catch(() => {});
  }
}

export async function connect() {
  const wv = globalThis.chrome?.webview;
  if (wv) return new Bridge(wv, false);
  const { createMockHost } = await import('./mock.js');
  return new Bridge(createMockHost(), true);
}
