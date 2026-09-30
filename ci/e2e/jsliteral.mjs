// Safe embedding of host-side values into JavaScript source that is evaluated in the page (CDP Runtime.evaluate).
//
// JSON.stringify alone is not enough for code construction: it leaves `<`, `>` and `/` as-is (so a value such
// as `</script><script>...` survives if the snippet ever lands in HTML) and U+2028/U+2029 raw (line terminators
// in older JS grammars). jsLiteral() escapes those as \uXXXX, which parse back to the exact same string.

const LS = String.fromCharCode(0x2028); // LINE SEPARATOR
const PS = String.fromCharCode(0x2029); // PARAGRAPH SEPARATOR
const UNSAFE_RE = new RegExp(`[<>/${LS}${PS}]`, 'g');
const toUnicodeEscape = (c) => `\\u${c.charCodeAt(0).toString(16).toUpperCase().padStart(4, '0')}`;

/** A JavaScript literal expression that evaluates to `value` (JSON-serialisable; undefined -> `undefined`). */
export function jsLiteral(value) {
  const json = JSON.stringify(value);
  if (json === undefined) return 'undefined';
  return json.replace(UNSAFE_RE, toUnicodeEscape);
}
