// Usage (from the worktree root): node commits.mjs [<base-ref>]
// Checks every commit message of <base-ref>..HEAD (default origin/main) with the repository's own rules
// (.github/scripts/conventions.mjs): Conventional Commits header of at most 100 characters and no AI attribution trailer.
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const base = process.argv[2] ?? 'origin/main';
const root = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
const { checkCommitMessage } = await import(pathToFileURL(resolve(root, '.github/scripts/conventions.mjs')).href);

const shas = execFileSync('git', ['rev-list', `${base}..HEAD`], { encoding: 'utf8' }).split('\n').filter(Boolean);
let failed = false;
for (const sha of shas) {
  const message = execFileSync('git', ['log', '-1', '--format=%B', sha], { encoding: 'utf8' });
  for (const problem of checkCommitMessage(message)) {
    failed = true;
    console.log(`${sha.slice(0, 12)}: ${problem}`);
  }
}
console.log(failed ? 'commit messages: FAIL' : `commit messages: ${shas.length} checked`);
process.exit(failed ? 1 : 0);
