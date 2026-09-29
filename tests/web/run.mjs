// Portable runner for the web-UI tests: `node tests/web/run.mjs` works on Node 20, 22 and 24 in any shell.
// (`node --test tests/web/` only works on Node 20: from Node 22 on, --test arguments are files/globs,
//  so use `node --test "tests/web/*.test.mjs"` there — Node expands the quoted glob itself.)
import { run } from 'node:test';
import { spec } from 'node:test/reporters';
import { readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const dir = path.dirname(fileURLToPath(import.meta.url));
const files = readdirSync(dir).filter((f) => f.endsWith('.test.mjs')).sort().map((f) => path.join(dir, f));
const reporter = /^class\b/.test(Function.prototype.toString.call(spec)) ? new spec() : spec;

run({ files, concurrency: true })
  .on('test:fail', () => { process.exitCode = 1; })
  .compose(reporter)
  .pipe(process.stdout);
