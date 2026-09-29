// Tiny lexical scanners (no parser, no dependencies) used by the web lint.
//
// scanJs(src)  -> { code, bare }  both the same length as src, newlines preserved:
//   code: comments blanked, string/template/regex literals kept verbatim
//   bare: comments AND literal contents blanked (quotes kept, `${...}` expressions kept as code)
// scanCs(src)  -> { code, bare }  same idea for C# ("...", @"...", $"...{expr}...", '.', """raw""").
//
// Limits (documented in ci/web/README.md): regex-vs-division is decided by the previous significant
// token (standard heuristic); JSX/HTML comments inside JS are not a thing here; C# raw interpolated
// strings ($"""...""") are treated as plain raw strings.

const blank = (c) => (c === '\n' || c === '\r' ? c : ' ');
const isIdent = (c) => /[\w$]/.test(c);
const REGEX_AFTER_WORD = new Set([
  'return', 'typeof', 'instanceof', 'in', 'of', 'new', 'delete', 'void', 'throw', 'case', 'do', 'else', 'yield', 'await',
]);

export function scanJs(src) {
  const n = src.length;
  const code = new Array(n);
  const bare = new Array(n);
  const stack = []; // '{' for code braces, 'tpl' for template `${`
  let i = 0;
  let mode = 'code';
  let prev = '';     // last significant char class: '' | 'w' (identifier/number) | 'v' (value literal) | the punctuator
  let word = '';     // last identifier
  const keep = (k) => { code[k] = src[k]; bare[k] = src[k]; };
  const lit = (k) => { code[k] = src[k]; bare[k] = blank(src[k]); };
  const hide = (k) => { code[k] = blank(src[k]); bare[k] = blank(src[k]); };

  const regexAllowed = () => {
    if (prev === '' || prev === 'op') return true;
    if (prev === 'w') return REGEX_AFTER_WORD.has(word);
    if (prev === 'v' || prev === ')' || prev === ']') return false;
    return true; // ( , = : [ ! & | ? { } ; + - * % < > ~ ^
  };

  /** End index (exclusive) of a regex literal starting at `start` ('/'), or -1 when it is not one. */
  const regexEnd = (start) => {
    let k = start + 1, inClass = false;
    for (; k < n; k++) {
      const c = src[k];
      if (c === '\n') return -1;
      if (c === '\\') { k++; continue; }
      if (inClass) { if (c === ']') inClass = false; continue; }
      if (c === '[') inClass = true;
      else if (c === '/') break;
    }
    if (k >= n) return -1;
    k++;
    while (k < n && /[a-z]/i.test(src[k])) k++;
    return k;
  };

  while (i < n) {
    const c = src[i];
    if (mode === 'tpl') {
      if (c === '\\') { lit(i); if (i + 1 < n) lit(i + 1); i += 2; continue; }
      if (c === '`') { keep(i); i++; mode = 'code'; prev = 'v'; continue; }
      if (c === '$' && src[i + 1] === '{') { keep(i); keep(i + 1); i += 2; stack.push('tpl'); mode = 'code'; prev = ''; continue; }
      lit(i); i++; continue;
    }
    // ---- code mode
    if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') hide(i++); continue; }
    if (c === '/' && src[i + 1] === '*') {
      hide(i); hide(i + 1); i += 2;
      while (i < n && !(src[i] === '*' && src[i + 1] === '/')) hide(i++);
      if (i < n) { hide(i); hide(i + 1); i += 2; }
      continue;
    }
    if (c === '\'' || c === '"') {
      keep(i); i++;
      while (i < n && src[i] !== c && src[i] !== '\n') {
        if (src[i] === '\\') { lit(i); i++; if (i < n) lit(i); i++; continue; }
        lit(i); i++;
      }
      if (i < n) { keep(i); i++; }
      prev = 'v'; continue;
    }
    if (c === '`') { keep(i); i++; mode = 'tpl'; continue; }
    if (c === '/' && regexAllowed()) {
      const end = regexEnd(i);
      if (end > 0) {
        keep(i);
        for (let k = i + 1; k < end; k++) lit(k);
        // keep closing slash + flags visible
        let close = end - 1;
        while (close > i && src[close] !== '/') close--;
        for (let k = close; k < end; k++) keep(k);
        i = end; prev = 'v'; continue;
      }
    }
    keep(i);
    if (c === '{') { stack.push('{'); prev = '{'; }
    else if (c === '}') {
      const top = stack.pop();
      if (top === 'tpl') { mode = 'tpl'; i++; continue; }
      prev = '}';
    } else if (isIdent(c)) {
      let k = i;
      while (k + 1 < n && isIdent(src[k + 1])) { k++; keep(k); }
      word = src.slice(i, k + 1); prev = 'w'; i = k + 1; continue;
    } else if (!/\s/.test(c)) {
      prev = c === ')' || c === ']' ? c : (c === '.' ? '.' : 'op');
    }
    i++;
  }
  return { code: code.join(''), bare: bare.join('') };
}

export function scanCs(src) {
  const n = src.length;
  const code = new Array(n);
  const bare = new Array(n);
  const keep = (k) => { code[k] = src[k]; bare[k] = src[k]; };
  const lit = (k) => { code[k] = src[k]; bare[k] = blank(src[k]); };
  const hide = (k) => { code[k] = blank(src[k]); bare[k] = blank(src[k]); };
  // stack of string contexts we must return to after an interpolation hole: { verbatim }
  const holes = [];
  let depth = 0; // brace depth inside the current interpolation hole
  let i = 0;

  function readString(verbatim, interp) {
    // i points at the first char after the opening quote
    while (i < n) {
      const c = src[i];
      if (!verbatim && c === '\\') { lit(i); if (i + 1 < n) lit(i + 1); i += 2; continue; }
      if (c === '"') {
        if (verbatim && src[i + 1] === '"') { lit(i); lit(i + 1); i += 2; continue; }
        keep(i); i++; return null;
      }
      if (interp && c === '{') {
        if (src[i + 1] === '{') { lit(i); lit(i + 1); i += 2; continue; }
        keep(i); i++; return { verbatim, interp };
      }
      if (!verbatim && c === '\n') return null; // unterminated
      lit(i); i++;
    }
    return null;
  }

  while (i < n) {
    const c = src[i];
    if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') hide(i++); continue; }
    if (c === '/' && src[i + 1] === '*') {
      hide(i); hide(i + 1); i += 2;
      while (i < n && !(src[i] === '*' && src[i + 1] === '/')) hide(i++);
      if (i < n) { hide(i); hide(i + 1); i += 2; }
      continue;
    }
    if (c === '"' && src.startsWith('"""', i)) {
      keep(i); keep(i + 1); keep(i + 2); i += 3;
      while (i < n && !src.startsWith('"""', i)) { lit(i); i++; }
      for (let k = 0; k < 3 && i < n; k++) keep(i++);
      continue;
    }
    const pre = src.slice(i, i + 3);
    let verbatim = false, interp = false, open = 0;
    if (c === '"') open = 1;
    else if (/^(\$@|@\$)"/.test(pre)) { verbatim = true; interp = true; open = 3; }
    else if (pre.startsWith('@"')) { verbatim = true; open = 2; }
    else if (pre.startsWith('$"')) { interp = true; open = 2; }
    if (open) {
      for (let k = 0; k < open; k++) keep(i + k);
      i += open;
      const hole = readString(verbatim, interp);
      if (hole) { holes.push({ ...hole, depth }); depth = 0; }
      continue;
    }
    if (c === '\'') {
      keep(i); i++;
      while (i < n && src[i] !== '\'' && src[i] !== '\n') { if (src[i] === '\\') { lit(i); i++; } if (i < n) { lit(i); i++; } }
      if (i < n) { keep(i); i++; }
      continue;
    }
    keep(i);
    if (holes.length) {
      if (c === '{') depth++;
      else if (c === '}') {
        if (depth === 0) {
          const h = holes.pop();
          depth = h.depth; i++;
          const again = readString(h.verbatim, h.interp);
          if (again) { holes.push({ ...again, depth }); depth = 0; }
          continue;
        }
        depth--;
      }
    }
    i++;
  }
  return { code: code.join(''), bare: bare.join('') };
}

/** Returns (index) => 1-based line number. */
export function lineIndex(src) {
  const starts = [0];
  for (let k = 0; k < src.length; k++) if (src[k] === '\n') starts.push(k + 1);
  return (idx) => {
    let lo = 0, hi = starts.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (starts[mid] <= idx) lo = mid; else hi = mid - 1; }
    return lo + 1;
  };
}

/** Index of the brace matching the '{' at `open` in a `bare` view, or -1. */
export function matchBrace(bare, open) {
  let d = 0;
  for (let k = open; k < bare.length; k++) {
    if (bare[k] === '{') d++;
    else if (bare[k] === '}' && --d === 0) return k;
  }
  return -1;
}

/** Brace depth at every index of a `bare` view (depth before the char). */
export function braceDepths(bare) {
  const out = new Int32Array(bare.length + 1);
  let d = 0;
  for (let k = 0; k < bare.length; k++) {
    out[k] = d;
    if (bare[k] === '{') d++;
    else if (bare[k] === '}') d--;
  }
  out[bare.length] = d;
  return out;
}

/** Splits "a, b as c, default as d" into [{ imported, local }]. */
export function specifiers(list) {
  return list.split(',').map((s) => s.trim()).filter(Boolean).map((s) => {
    const m = /^([\w$]+)(?:\s+as\s+([\w$]+))?$/.exec(s.replace(/\s+/g, ' '));
    return m ? { imported: m[1], local: m[2] || m[1] } : { imported: s, local: s, bad: true };
  });
}
