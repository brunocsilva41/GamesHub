// Minimal event emitter shared by the bridge and the store (DOM-free).
export class Emitter {
  constructor() { this._handlers = new Map(); }

  on(name, fn) {
    if (!this._handlers.has(name)) this._handlers.set(name, new Set());
    this._handlers.get(name).add(fn);
    return () => this.off(name, fn);
  }

  off(name, fn) {
    const set = this._handlers.get(name);
    if (set) set.delete(fn);
  }

  emit(name, data) {
    const set = this._handlers.get(name);
    if (!set) return;
    for (const fn of [...set]) {
      try { fn(data); }
      catch (err) { console.error(`[emitter] handler for "${name}" failed`, err); }
    }
  }
}
