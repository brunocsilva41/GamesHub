// Both WebView pages (main window and quick-launch palette) lock down <base> and form targets.
import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { readRepo } from './_setup.mjs';

const csp = (rel) => {
  const m = readRepo(rel).match(/http-equiv="Content-Security-Policy"\s+content="([^"]+)"/);
  assert.ok(m, `${rel} has a CSP meta tag`);
  return m[1].split(';').map((d) => d.trim()).filter(Boolean);
};

describe('Content-Security-Policy', () => {
  for (const page of ['web/index.html', 'web/quick/index.html']) {
    test(`${page} forbids <base> and form submissions`, () => {
      const d = csp(page);
      assert.ok(d.includes("base-uri 'none'"), d.join('; '));
      assert.ok(d.includes("form-action 'none'"), d.join('; '));
      assert.ok(d.includes("script-src 'self'"), 'no inline/remote scripts');
    });
  }
});
