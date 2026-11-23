// Usage: node check-mermaid.mjs <file.md>...
// Parses every ```mermaid block of the given Markdown files with the real Mermaid parser and exits 1 when any block is invalid.
// Run it on a PR body before `gh pr create` / `gh pr edit`. mermaid and jsdom are installed into a cache directory on first use.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const files = process.argv.slice(2);
if (files.length === 0) {
  console.error('usage: check-mermaid.mjs <file.md>...');
  process.exit(64);
}

const cache = process.env.FORGE_MERMAID_DIR ?? join(tmpdir(), 'forge-mermaid');
if (!existsSync(join(cache, 'node_modules', 'mermaid'))) {
  mkdirSync(cache, { recursive: true });
  writeFileSync(join(cache, 'package.json'), '{"name":"forge-mermaid","private":true}');
  execFileSync('npm', ['install', '--no-audit', '--no-fund', '--silent', 'mermaid@11.4.1', 'jsdom@24.1.3'], { cwd: cache, stdio: 'inherit' });
}

const require = createRequire(join(cache, 'package.json'));
const { JSDOM } = require('jsdom');
const dom = new JSDOM('<!doctype html><html><body></body></html>');
globalThis.window = dom.window;
globalThis.document = dom.window.document;
Object.defineProperty(globalThis, 'navigator', { value: dom.window.navigator, configurable: true });
const mermaid = (await import(join(cache, 'node_modules', 'mermaid', 'dist', 'mermaid.core.mjs'))).default;

// Fenced blocks: an opening ```mermaid line up to the next closing fence.
const FENCE = /^```mermaid[^\n]*\n([\s\S]*?)^```[ \t]*$/gm;
let blocks = 0;
let invalid = 0;
for (const file of files) {
  const text = readFileSync(file, 'utf8');
  for (const match of text.matchAll(FENCE)) {
    blocks++;
    const line = text.slice(0, match.index).split('\n').length;
    try {
      await mermaid.parse(match[1]);
    } catch (error) {
      invalid++;
      console.log(`${file}:${line}: invalid mermaid: ${String(error.message ?? error).split('\n').slice(0, 4).join(' | ')}`);
    }
  }
}

console.log(`mermaid: ${blocks} block(s), ${invalid} invalid`);
process.exit(invalid > 0 || blocks === 0 ? 1 : 0);
