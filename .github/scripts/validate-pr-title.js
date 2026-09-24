// Validates that the PR title follows Conventional Commits and contains no
// emojis or other decorative non-ASCII symbols.
'use strict';

const { CONVENTIONAL_COMMIT_RE, NON_ASCII_RE } = require('./constants');

module.exports = async ({ context, core }) => {
  const title = context.payload.pull_request.title;

  const errors = [];
  if (!CONVENTIONAL_COMMIT_RE.test(title)) {
    errors.push(
      'Title must follow Conventional Commits: ' +
        '"<type>(<optional scope>)!: <description>" ' +
        '(type is one of feat|fix|refactor|perf|test|docs|build|ci|chore|revert|style).'
    );
  }
  if (NON_ASCII_RE.test(title)) {
    errors.push('Title must not contain emojis or other non-ASCII characters.');
  }

  if (errors.length > 0) {
    core.setFailed(`PR title "${title}" is invalid:\n- ${errors.join('\n- ')}`);
    return;
  }

  core.summary.addHeading('PR title check', 3).addRaw(`Title "${title}" is valid.`).write();
};
