// Regression tests for the Code Scanning (CodeQL) security fixes in the lint/e2e tooling:
// regex escaping of scanned identifiers, HTML tag handling in the HTML check, and safe embedding of
// values into page-evaluated JavaScript (CDP).
import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { escapeRegExp } from '../../ci/web/lib/scan.mjs';
import { parseModule } from '../../ci/web/lib/modules.mjs';
import { uiCommands, findDefinition } from '../../ci/web/lib/contract.mjs';
import { checkHtml, visibleText } from '../../ci/web/lib/static-checks.mjs';
import { jsLiteral } from '../../ci/e2e/jsliteral.mjs';
import { runInNewContext } from 'node:vm';

const LS = String.fromCharCode(0x2028);
const PS = String.fromCharCode(0x2029);
const UNSAFE_OUT = new RegExp(`[<>${LS}${PS}]`);

describe('escapeRegExp (incomplete-sanitization: backslash and every metacharacter)', () => {
  const nasty = ['a\\b', 'x$', '(.*)', 'a.b', '[a-z]+', '{2}', 'a|b', '^\\d?$', '\\', 'evil.com.steamgriddb.com'];
  for (const s of nasty) {
    test(`matches ${JSON.stringify(s)} literally and nothing else`, () => {
      const re = new RegExp(`^${escapeRegExp(s)}$`);
      assert.ok(re.test(s));
      assert.equal(re.test(`${s}x`), false);
    });
  }
  test('keeps ordinary identifiers unchanged', () => {
    assert.equal(escapeRegExp('bridgeCall'), 'bridgeCall');
    assert.equal(escapeRegExp('$el'), '\\$el');
  });
  test('findDefinition treats the name literally ("a.b" does not match "axb")', () => {
    const files = new Map([['f.js', 'const o = { axb() {} };\n']]);
    assert.equal(findDefinition(files, 'a.b'), null);
    assert.equal(findDefinition(files, 'a\\'), null);
    assert.deepEqual(findDefinition(files, 'axb'), { file: 'f.js', line: 1 });
  });
  test('uiCommands survives a non-identifier call argument (was an unescaped "(" -> SyntaxError)', () => {
    const files = new Map([['f.js', 'export function go(k) { return bridge.call(pick(k), {}); }\nexport const x = () => bridge.call(a\\b, {});\n']]);
    assert.doesNotThrow(() => uiCommands(files));
  });
  test('namespace uses still resolve for $-prefixed namespaces', () => {
    const m = parseModule("import * as $A from './x.js';\n$A.run();\n");
    assert.deepEqual(m.nsUses.map((u) => u.name), ['run']);
  });
});

describe('HTML check (bad-tag-filter / incomplete-multi-character-sanitization)', () => {
  const lint = (src) => {
    const out = [];
    checkHtml([{ rel: 'web/x.html', src }], (level, file, line, check, msg) => out.push({ level, line, check, msg }));
    return out.filter((p) => /inline <script>/.test(p.msg));
  };
  const page = (body) => `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"></head><body>${body}</body></html>`;

  test('inline script closed by "</script >" is reported', () => {
    assert.equal(lint(page('<script>alert(1)</script >')).length, 1);
  });
  test('inline script closed by "</script foo=bar>" is reported', () => {
    assert.equal(lint(page('<script>alert(1)</script foo="bar">')).length, 1);
  });
  test('unterminated inline script is reported', () => {
    assert.equal(lint(page('<script>alert(1)')).length, 1);
  });
  test('external scripts are still fine', () => {
    assert.equal(lint(page('<script type="module" src="js/main.js"></script >')).length, 0);
  });

  test('visibleText never leaves a tag behind (<scr<script>ipt>)', () => {
    for (const s of ['<scr<script>ipt>alert(1)</script>', '<<script>script>x', '<img src=x onerror=alert(1)>', '"><script>']) {
      const t = visibleText(s);
      assert.equal(/<|>/.test(t), false, `${s} -> ${t}`);
      assert.equal(/<script/i.test(t), false);
    }
  });
  test('visibleText keeps behaviour for normal buttons', () => {
    assert.equal(visibleText('<span class="i">Jogar</span>'), 'Jogar');
    assert.equal(visibleText('<svg viewBox="0 0 1 1"><path d="M0 0"/></svg >'), '');
    assert.equal(visibleText('<svg><title>x</title></svg>&nbsp;Sair'), 'Sair');
  });
  test('a button whose only content is a split/broken tag is still unnamed', () => {
    const out = [];
    checkHtml([{ rel: 'web/x.html', src: page('<button><scr<b>ipt></button>') }], (l, f, ln, c, msg) => out.push(msg));
    assert.ok(out.some((m) => /<button> without text or aria-label/.test(m)), out.join('\n'));
  });
});

describe('jsLiteral (bad-code-sanitization in the CDP e2e snippets)', () => {
  const evil = [
    '"); alert(1); ("',
    "'); alert(1); ('",
    '</script><script>alert(1)</script>',
    '"><img src=x onerror=alert(1)>',
    'javascript:alert(1)',
    'back\\slash\\"',
    `line${LS}sep${PS}para`,
    '#view-game [data-x="1"]',
  ];
  for (const s of evil) {
    test(`round-trips ${JSON.stringify(s)} as a single literal`, () => {
      const lit = jsLiteral(s);
      assert.equal(UNSAFE_OUT.test(lit), false, lit);
      assert.equal(lit.includes('</'), false);
      assert.equal(runInNewContext(`(${lit})`), s); // evaluates to exactly the input, no code executed
    });
  }
  test('non-strings and undefined', () => {
    assert.equal(jsLiteral(5), '5');
    assert.equal(jsLiteral(null), 'null');
    assert.equal(jsLiteral(undefined), 'undefined');
    assert.deepEqual({ ...runInNewContext(`(${jsLiteral({ id: 'a</b>' })})`) }, { id: 'a</b>' });
  });
});
