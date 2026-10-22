// Conventional Commits policy shared by CI (scripts/git/lint-commits.sh) and local hooks.
export default {
  extends: ['@commitlint/config-conventional'],
  // Dependabot commits are generated and carry release notes with long lines. Their PR titles
  // are still checked by the PR hygiene workflow, and squash merges use the title.
  ignores: [(message) => message.includes('Signed-off-by: dependabot[bot]')],
  rules: {
    'type-enum': [
      2,
      'always',
      ['feat', 'fix', 'docs', 'style', 'refactor', 'perf', 'test', 'build', 'ci', 'chore', 'revert'],
    ],
    'header-max-length': [2, 'always', 100],
  },
};
