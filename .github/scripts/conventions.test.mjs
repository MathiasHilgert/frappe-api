import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkBranch, checkCommitMessage, checkHeader, findAiAttribution } from './conventions.mjs';

test('a header with type, scope and a lower-case summary is valid', () => {
  const result = checkHeader('feat(orders): list orders per customer and year');
  assert.deepEqual(result.problems, []);
  assert.equal(result.type, 'feat');
  assert.equal(result.scope, 'orders');
  assert.equal(result.breaking, false);
});

test('a breaking header is marked with "!" before the colon', () => {
  const result = checkHeader('feat(http)!: problems through IProblemDetailsService with a fixed contract');
  assert.deepEqual(result.problems, []);
  assert.equal(result.breaking, true);
});

test('the scope is mandatory', () => {
  assert.equal(checkHeader('feat: list orders').problems.length, 1);
});

test('an unknown type, an upper-case scope or a missing space is refused', () => {
  for (const header of ['feature(orders): x', 'feat(Orders): x', 'feat(orders):x', 'Feat(orders): x']) {
    assert.equal(checkHeader(header).problems.length, 1, header);
  }
});

test('the summary starts lower-case, has no trailing period and the header fits in 100 characters', () => {
  assert.deepEqual(checkHeader('fix(orders): List orders').problems, ['the summary starts with a lower-case letter']);
  assert.deepEqual(checkHeader('fix(orders): list orders.').problems, ['the summary has no trailing period']);
  assert.equal(checkHeader(`fix(orders): ${'a'.repeat(95)}`).problems.length, 1);
});

test('a summary may start with an acronym or a name written in capitals', () => {
  for (const header of ['feat(http): HEAD by convention', 'docs(api): OpenAPI examples', 'fix(orders): EF Core reads']) {
    assert.deepEqual(checkHeader(header).problems, [], header);
  }
});

test('AI co-author trailers, generated-with footers and session links are found', () => {
  const message = [
    'feat(orders): list orders',
    '',
    'Body.',
    '',
    'Co-Authored-By: Claude Opus <noreply@anthropic.com>',
    'Co-authored-by: Copilot <copilot@github.com>',
    '🤖 Generated with [Claude Code](https://claude.com/claude-code)',
    'Claude-Session: https://claude.ai/code/session_1',
  ].join('\n');
  assert.equal(findAiAttribution(message).length, 4);
  assert.equal(checkCommitMessage(message).length, 4);
});

test('a human co-author is not AI attribution', () => {
  assert.deepEqual(checkCommitMessage('fix(orders): x\n\nCo-authored-by: Ana Pérez <ana@example.com>'), []);
});

test('a branch is <type>/<kebab-topic> with the type of the title', () => {
  assert.deepEqual(checkBranch('feat/orders-listing', 'feat'), []);
  assert.deepEqual(checkBranch('ci/pipeline', 'ci'), []);
  assert.equal(checkBranch('feature/orders', 'feat').length, 1);
  assert.equal(checkBranch('feat/Orders_Listing', 'feat').length, 1);
  assert.equal(checkBranch('fix/orders-listing', 'feat').length, 1);
});

test('tool branches pass whatever their name', () => {
  for (const branch of ['renovate/npm-x', 'dependabot/nuget/x', 'release-please--branches--main']) {
    assert.deepEqual(checkBranch(branch, 'chore'), [], branch);
  }
});
