import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkBranch, checkCommitMessage, checkHeader, findAiAttribution } from './conventions.mjs';

test('a header with type, scope and a lower-case summary is valid', () => {
  const result = checkHeader('feat(geo): serve national holidays per country and year');
  assert.deepEqual(result.problems, []);
  assert.equal(result.type, 'feat');
  assert.equal(result.scope, 'geo');
  assert.equal(result.breaking, false);
});

test('a breaking header is marked with "!" before the colon', () => {
  const result = checkHeader('feat(http)!: problems through IProblemDetailsService with a fixed contract');
  assert.deepEqual(result.problems, []);
  assert.equal(result.breaking, true);
});

test('the scope is mandatory', () => {
  assert.equal(checkHeader('feat: serve holidays').problems.length, 1);
});

test('an unknown type, an upper-case scope or a missing space is refused', () => {
  for (const header of ['feature(geo): x', 'feat(Geo): x', 'feat(geo):x', 'Feat(geo): x']) {
    assert.equal(checkHeader(header).problems.length, 1, header);
  }
});

test('the summary starts lower-case, has no trailing period and the header fits in 100 characters', () => {
  assert.deepEqual(checkHeader('fix(geo): Serve holidays').problems, ['the summary starts with a lower-case letter']);
  assert.deepEqual(checkHeader('fix(geo): serve holidays.').problems, ['the summary has no trailing period']);
  assert.equal(checkHeader(`fix(geo): ${'a'.repeat(95)}`).problems.length, 1);
});

test('a summary may start with an acronym or a name written in capitals', () => {
  for (const header of ['feat(http): HEAD by convention', 'docs(api): OpenAPI examples', 'fix(geo): EF Core reads']) {
    assert.deepEqual(checkHeader(header).problems, [], header);
  }
});

test('AI co-author trailers, generated-with footers and session links are found', () => {
  const message = [
    'feat(geo): serve holidays',
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
  assert.deepEqual(checkCommitMessage('fix(geo): x\n\nCo-authored-by: Ana Pérez <ana@example.com>'), []);
});

test('a branch is <type>/<kebab-topic> with the type of the title', () => {
  assert.deepEqual(checkBranch('feat/geo-holidays', 'feat'), []);
  assert.deepEqual(checkBranch('ci/pipeline', 'ci'), []);
  assert.equal(checkBranch('feature/geo', 'feat').length, 1);
  assert.equal(checkBranch('feat/Geo_Holidays', 'feat').length, 1);
  assert.equal(checkBranch('fix/geo-holidays', 'feat').length, 1);
});

test('tool branches pass whatever their name', () => {
  for (const branch of ['renovate/npm-x', 'dependabot/nuget/x', 'release-please--branches--main']) {
    assert.deepEqual(checkBranch(branch, 'chore'), [], branch);
  }
});
