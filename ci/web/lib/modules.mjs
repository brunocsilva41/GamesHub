// ES-module graph checks over web/**/*.js: relative imports resolve, named imports exist as exports,
// namespace member accesses (`import * as A` ... `A.run`) exist as exports.
//
// Regex-over-scanned-source, not a real parser. Supported export forms:
//   export [async] function[*] x | export class X | export const|let|var x | export const { a, b: c } = ...
//   export default ... | export { a, b as c } | export { a } from '...' | export * [as ns] from '...'
// Not supported (documented): multiple declarators in one `export const a = 1, b = 2`
// (only `a` is seen), array-destructuring exports.
import { existsSync, statSync } from 'node:fs';
import path from 'node:path';
import { scanJs, lineIndex, specifiers } from './scan.mjs';

const NB = '(?<![\\w$.])';
const IMPORT_RE = new RegExp(`${NB}import(?![\\w$])\\s*(?:([\\w$]+)\\s*,?\\s*)?(?:\\{([^}]*)\\}|\\*\\s*as\\s+([\\w$]+))?\\s*(?:from\\s*)?(['"])([^'"\\n]+)\\4`, 'g');
const EXPORT_FROM_RE = new RegExp(`${NB}export(?![\\w$])\\s*(?:\\*\\s*(?:as\\s+([\\w$]+)\\s*)?|\\{([^}]*)\\})\\s*from\\s*(['"])([^'"\\n]+)\\3`, 'g');
const DYN_RE = new RegExp(`${NB}import\\s*\\(\\s*(['"\`])([^'"\`\\n]+)\\1\\s*\\)`, 'g');
const DYN_DESTRUCT_RE = /\{([^{}]*)\}\s*=\s*await\s+import\s*\(\s*(['"`])([^'"`\n]+)\2\s*\)/g;

/** Parses one module. Returns { imports, exports:Set, starFrom:[], namespaces:[] }. */
export function parseModule(src) {
  const { code, bare } = scanJs(src);
  const line = lineIndex(src);
  const imports = [];   // { spec, names:[{imported}], line, kind }
  const exportNames = new Set();
  const starFrom = [];  // specs re-exported with `export *`
  const namespaces = []; // { local, spec }

  for (const m of code.matchAll(IMPORT_RE)) {
    // skip matches whose keyword sits inside a literal (bare view blanks those)
    if (bare.slice(m.index, m.index + 6) !== 'import') continue;
    const names = [];
    if (m[1]) names.push({ imported: 'default', local: m[1] });
    if (m[2] !== undefined) names.push(...specifiers(m[2]));
    if (m[3]) namespaces.push({ local: m[3], spec: m[5] });
    imports.push({ spec: m[5], names, line: line(m.index), kind: 'static' });
  }
  for (const m of code.matchAll(EXPORT_FROM_RE)) {
    if (bare.slice(m.index, m.index + 6) !== 'export') continue;
    const spec = m[4];
    if (m[2] !== undefined) {
      const sp = specifiers(m[2]);
      imports.push({ spec, names: sp, line: line(m.index), kind: 'reexport' });
      for (const s of sp) exportNames.add(s.local);
    } else if (m[1]) {
      imports.push({ spec, names: [], line: line(m.index), kind: 'reexport' });
      exportNames.add(m[1]);
    } else {
      imports.push({ spec, names: [], line: line(m.index), kind: 'reexport' });
      starFrom.push(spec);
    }
  }
  const destructured = new Map();
  for (const m of code.matchAll(DYN_DESTRUCT_RE)) {
    destructured.set(m.index + m[0].lastIndexOf('import'), m[1]);
  }
  for (const m of code.matchAll(DYN_RE)) {
    if (bare.slice(m.index, m.index + 6) !== 'import') continue;
    const list = destructured.get(m.index);
    const names = list ? list.split(',').map((s) => s.split(':')[0].trim()).filter(Boolean).map((imported) => ({ imported })) : [];
    imports.push({ spec: m[2], names, line: line(m.index), kind: 'dynamic' });
  }

  for (const m of bare.matchAll(new RegExp(`${NB}export\\s+(?:async\\s+)?function\\s*\\*?\\s*([\\w$]+)`, 'g'))) exportNames.add(m[1]);
  for (const m of bare.matchAll(new RegExp(`${NB}export\\s+class\\s+([\\w$]+)`, 'g'))) exportNames.add(m[1]);
  for (const m of bare.matchAll(new RegExp(`${NB}export\\s+(?:const|let|var)\\s+([\\w$]+)`, 'g'))) exportNames.add(m[1]);
  for (const m of bare.matchAll(new RegExp(`${NB}export\\s+(?:const|let|var)\\s*\\{([^}]*)\\}`, 'g'))) {
    for (const part of m[1].split(',')) {
      const name = part.split('=')[0].split(':').pop().trim();
      if (name) exportNames.add(name);
    }
  }
  if (new RegExp(`${NB}export\\s+default\\b`).test(bare)) exportNames.add('default');
  for (const m of bare.matchAll(new RegExp(`${NB}export\\s*\\{([^}]*)\\}(?!\\s*from)`, 'g'))) {
    for (const s of specifiers(m[1])) exportNames.add(s.local);
  }

  // namespace member uses: A.foo
  const nsUses = [];
  for (const ns of namespaces) {
    const re = new RegExp(`${NB}${ns.local.replace(/\$/g, '\\$')}\\s*\\.\\s*([\\w$]+)`, 'g');
    for (const m of bare.matchAll(re)) nsUses.push({ ns: ns.local, spec: ns.spec, name: m[1], line: line(m.index) });
  }
  return { imports, exports: exportNames, starFrom, namespaces, nsUses };
}

/** Resolves a specifier from `fromFile`; absolute '/x' resolves against `webRoot`. Returns path or null. */
export function resolveSpec(spec, fromFile, webRoot) {
  if (spec.startsWith('./') || spec.startsWith('../')) return path.resolve(path.dirname(fromFile), spec);
  if (spec.startsWith('/')) return path.join(webRoot, spec);
  return null;
}

const isFile = (p) => { try { return existsSync(p) && statSync(p).isFile(); } catch { return false; } };

/**
 * Checks every module in `files` (absolute paths of .js files). `report(file, line, msg)` collects errors.
 */
export function checkModuleGraph(files, webRoot, sources, report) {
  const parsed = new Map();
  const get = (f) => {
    if (!parsed.has(f)) parsed.set(f, parseModule(sources.get(f) ?? ''));
    return parsed.get(f);
  };
  const exportsOf = (f, seen = new Set()) => {
    if (seen.has(f)) return new Set();
    seen.add(f);
    const mod = get(f);
    const all = new Set(mod.exports);
    for (const spec of mod.starFrom) {
      const target = resolveSpec(spec, f, webRoot);
      if (target && sources.has(target)) for (const x of exportsOf(target, seen)) if (x !== 'default') all.add(x);
    }
    return all;
  };
  let edges = 0;
  for (const file of files) {
    const mod = get(file);
    for (const imp of mod.imports) {
      edges++;
      if (/^[a-z][a-z0-9+.-]*:/i.test(imp.spec) || imp.spec.startsWith('//')) {
        report(file, imp.line, `import of a URL "${imp.spec}" (the app is offline; no CDN)`);
        continue;
      }
      const target = resolveSpec(imp.spec, file, webRoot);
      if (!target) { report(file, imp.line, `bare import specifier "${imp.spec}" (no bundler: use a relative path)`); continue; }
      if (!isFile(target)) { report(file, imp.line, `import "${imp.spec}" does not resolve to a file`); continue; }
      if (!target.endsWith('.js') && !target.endsWith('.mjs')) continue;
      if (!sources.has(target)) continue;
      const names = exportsOf(target);
      for (const n of imp.names) {
        if (n.bad) report(file, imp.line, `cannot parse import specifier "${n.imported}"`);
        else if (!names.has(n.imported)) report(file, imp.line, `"${n.imported}" is not exported by ${imp.spec}`);
      }
    }
    for (const use of mod.nsUses) {
      const target = resolveSpec(use.spec, file, webRoot);
      if (!target || !sources.has(target)) continue;
      if (!exportsOf(target).has(use.name)) report(file, use.line, `${use.ns}.${use.name}: "${use.name}" is not exported by ${use.spec}`);
    }
  }
  return { modules: files.length, edges };
}
