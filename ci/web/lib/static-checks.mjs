// File-level static checks: HTML/CSS local references, network URLs, code hygiene, size budgets, HTML a11y.
// Each check receives { rel, abs, src } file records and a `report(level, rel, line, check, msg)` sink.
import { existsSync, statSync } from 'node:fs';
import path from 'node:path';
import { scanJs, lineIndex } from './scan.mjs';

export const BUDGETS = { jsLines: 300, cssLines: 700, totalBytes: 1.5 * 1024 * 1024 };

/** https hosts that may appear anywhere: the app's own virtual hosts + user-facing links opened via openExternal. */
export const ALLOWED_HOSTS = [
  'app.gameshub.example', 'art.gameshub.example', // WebView2 virtual hosts (never hit the network)
  'www.pcgamingwiki.com',                         // "Abrir no PCGamingWiki" (gameinfo.js, via openExternal)
  'www.steamgriddb.com',                          // "Obter chave da API" (settings.js, via openExternal)
  '*.steamstatic.com', 'steamcdn-a.akamaihd.net', // CSP img-src only: Steam search result icons (index.html)
];
/** Extra hosts tolerated only in dev-mode demo data (js/mock*.js): fake release page + sample store websites. */
export const DEMO_HOSTS = ['github.com', 'www.counter-strike.net', 'www.cyberpunk.net', 'www.eldenring.com', 'www.thewitcher.com'];
/** http:// is never fetched; these are XML namespace identifiers, not network URLs. */
export const XML_NAMESPACES = ['http://www.w3.org/2000/svg', 'http://www.w3.org/1999/xlink', 'http://www.w3.org/1999/xhtml'];

const isDemo = (rel) => /(^|\/)js\/mock[\w-]*\.js$/.test(rel);
const isFile = (p) => { try { return existsSync(p) && statSync(p).isFile(); } catch { return false; } };
const blankKeepLines = (s) => s.replace(/[^\n]/g, ' ');

function stripHtmlComments(src) { return src.replace(/<!--[\s\S]*?-->/g, blankKeepLines); }
function stripCssComments(src) { return src.replace(/\/\*[\s\S]*?\*\//g, blankKeepLines); }

function checkLocalRef(file, value, line, webRoot, report, what) {
  const v = value.trim();
  if (!v || v.startsWith('#') || /^data:/i.test(v)) return;
  if (/^javascript:/i.test(v)) { report('error', file.rel, line, 'refs', `${what} uses a javascript: URL (CSP)`); return; }
  if (/^[a-z][a-z0-9+.-]*:/i.test(v) || v.startsWith('//')) {
    report('error', file.rel, line, 'refs', `${what} "${v}" is not local (no network resources: the app works offline)`);
    return;
  }
  const clean = decodeURIComponent(v.replace(/[?#].*$/, ''));
  const target = clean.startsWith('/') ? path.join(webRoot, clean) : path.resolve(path.dirname(file.abs), clean);
  if (!target.startsWith(webRoot)) report('error', file.rel, line, 'refs', `${what} "${v}" points outside web/`);
  else if (!isFile(target)) report('error', file.rel, line, 'refs', `${what} "${v}" does not exist`);
}

/** src/href in HTML, url()/@import in CSS: every local reference must exist. */
export function checkRefs(files, webRoot, report) {
  let count = 0;
  for (const f of files) {
    const line = lineIndex(f.src);
    if (f.rel.endsWith('.html')) {
      const src = stripHtmlComments(f.src);
      for (const m of src.matchAll(/<[a-zA-Z][^>]*>/g)) {
        for (const a of m[0].matchAll(/\s(src|href)\s*=\s*(["'])(.*?)\2/gi)) {
          count++;
          checkLocalRef(f, a[3], line(m.index + a.index), webRoot, report, a[1]);
        }
      }
    } else if (f.rel.endsWith('.css')) {
      const src = stripCssComments(f.src);
      for (const m of src.matchAll(/url\(\s*(['"]?)([^'")]*)\1\s*\)/g)) { count++; checkLocalRef(f, m[2], line(m.index), webRoot, report, 'url()'); }
      for (const m of src.matchAll(/@import\s+(['"])([^'"]+)\1/g)) { count++; checkLocalRef(f, m[2], line(m.index), webRoot, report, '@import'); }
    }
  }
  return count;
}

/** No http:// (except XML namespaces) and no https:// outside the allowlist in HTML/CSS/JS. */
export function checkUrls(files, report) {
  let count = 0;
  for (const f of files) {
    if (!/\.(html|css|js|mjs)$/.test(f.rel)) continue;
    const line = lineIndex(f.src);
    for (const m of f.src.matchAll(/\b(https?):\/\/([^\s'"`)<>\\]*)/gi)) {
      count++;
      const url = m[0];
      if (m[1].toLowerCase() === 'http') {
        if (!XML_NAMESPACES.some((ns) => url === ns || url.startsWith(`${ns}"`))) {
          report('error', f.rel, line(m.index), 'urls', `insecure/network URL "${url}" (http:// is not allowed)`);
        }
        continue;
      }
      // ";" and "," end a URL inside CSP directives / srcset lists.
      const host = m[2].split(/[/?#:$;,]/)[0].toLowerCase();
      if (ALLOWED_HOSTS.includes(host)) continue;
      if (isDemo(f.rel) && DEMO_HOSTS.includes(host)) continue;
      report('error', f.rel, line(m.index), 'urls', `external URL "${url}" (host "${host}" is not in the allowlist in ci/web/lib/static-checks.mjs)`);
    }
  }
  return count;
}

const TODO_RE = /\b(TODO|FIXME|XXX)\b(?![^\n]*(#\d+|\b[A-Z][A-Z0-9]+-\d+\b|issues\/\d+))/g;

/** console.log/debug/trace, debugger statements; TODO/FIXME/XXX without an issue reference. */
export function checkHygiene(files, report) {
  for (const f of files) {
    const line = lineIndex(f.src);
    if (/\.m?js$/.test(f.rel)) {
      const { bare } = scanJs(f.src);
      for (const m of bare.matchAll(/(?<![\w$.])console\s*\.\s*(log|debug|trace|dir|table)\s*\(/g)) {
        report('error', f.rel, line(m.index), 'hygiene', `console.${m[1]}() left in shipped code (use console.warn/error or bridge.log)`);
      }
      for (const m of bare.matchAll(/(?<![\w$.])debugger(?![\w$])/g)) report('error', f.rel, line(m.index), 'hygiene', 'debugger statement');
    }
    if (/\.(html|css|m?js)$/.test(f.rel)) {
      for (const m of f.src.matchAll(TODO_RE)) {
        report('error', f.rel, line(m.index), 'hygiene', `${m[1]} without an issue reference (write "${m[1]}(#123): ...")`);
      }
    }
  }
}

export const countLines = (src) => (src.length === 0 ? 0 : src.split('\n').length - (src.endsWith('\n') ? 1 : 0));

/** Line budgets per JS/CSS file, total size of web/. Returns stats for the summary. */
export function checkBudgets(allFiles, report) {
  let total = 0;
  const sized = [];
  for (const f of allFiles) {
    total += f.size;
    sized.push(f);
    if (f.src === null) continue;
    const lines = countLines(f.src);
    f.lines = lines;
    if (/\.m?js$/.test(f.rel) && lines > BUDGETS.jsLines) {
      report('error', f.rel, lines, 'budget', `${lines} lines > ${BUDGETS.jsLines} (split the module)`);
    }
    if (f.rel.endsWith('.css') && lines > BUDGETS.cssLines) {
      report('error', f.rel, lines, 'budget', `${lines} lines > ${BUDGETS.cssLines} (split the stylesheet)`);
    }
  }
  if (total > BUDGETS.totalBytes) {
    report('error', 'web', 0, 'budget', `web/ is ${(total / 1024).toFixed(0)} KB > ${(BUDGETS.totalBytes / 1024).toFixed(0)} KB`);
  }
  sized.sort((a, b) => b.size - a.size);
  const near = allFiles.filter((f) => f.lines && ((/\.m?js$/.test(f.rel) && f.lines > BUDGETS.jsLines * 0.9)
    || (f.rel.endsWith('.css') && f.lines > BUDGETS.cssLines * 0.9)));
  return { total, largest: sized.slice(0, 5), near };
}

function attr(tag, name) {
  const m = new RegExp(`\\s${name}\\s*=\\s*(["'])(.*?)\\1|\\s${name}(?=[\\s>/])`, 'i').exec(tag);
  return m ? (m[2] ?? '') : null;
}

/** lang/charset, CSP-safe markup (no inline handlers/scripts), <img alt>, named <button>s. Also scans JS markup strings. */
export function checkHtml(files, report) {
  for (const f of files) {
    const line = lineIndex(f.src);
    if (f.rel.endsWith('.html')) {
      const src = stripHtmlComments(f.src);
      const html = /<html\b[^>]*>/i.exec(src);
      if (!html || attr(html[0], 'lang') !== 'pt-BR') report('error', f.rel, html ? line(html.index) : 1, 'html', '<html> must have lang="pt-BR"');
      if (!/<meta\s[^>]*charset\s*=/i.test(src)) report('error', f.rel, 1, 'html', 'missing <meta charset>');
      checkMarkup(f, src, 0, line, report);
      for (const m of src.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
        if (!/\ssrc\s*=/.test(m[1]) && m[2].trim()) report('error', f.rel, line(m.index), 'html', 'inline <script> (CSP: script-src \'self\')');
      }
    } else if (/\.m?js$/.test(f.rel)) {
      // markup built from string/template literals: `<button onclick=...>` or `<img>` without alt
      const { code, bare } = scanJs(f.src);
      let lit = '';
      for (let k = 0; k < code.length; k++) lit += code[k] !== bare[k] || code[k] === '\n' ? code[k] : ' ';
      checkMarkup(f, lit, 0, line, report, true);
    }
  }
}

function checkMarkup(f, src, offset, line, report, fromJs = false) {
  for (const m of src.matchAll(/<([a-zA-Z][\w-]*)(\s[^<>]*?)?\/?>/g)) {
    const tag = m[0];
    const at = line(offset + m.index);
    const h = /\s(on[a-z]+)\s*=/i.exec(tag);
    if (h) report('error', f.rel, at, 'html', `inline event handler ${h[1]}= (CSP-unsafe; use addEventListener)`);
    const name = m[1].toLowerCase();
    if (name === 'img' && attr(tag, 'alt') === null) report('error', f.rel, at, 'html', '<img> without alt (use alt="" for decorative images)');
    if (name === 'button' && !fromJs) {
      const end = src.indexOf('</button>', m.index);
      const inner = end > 0 ? src.slice(m.index + tag.length, end) : '';
      const text = inner.replace(/<svg[\s\S]*?<\/svg>/gi, '').replace(/<[^>]+>/g, '').replace(/&nbsp;/g, ' ').trim();
      if (!text && attr(tag, 'aria-label') === null && attr(tag, 'aria-labelledby') === null) {
        report('error', f.rel, at, 'html', '<button> without text or aria-label');
      }
    }
  }
}
