// Validates that the PR head branch follows "<type>/<short-description>".
// Dependabot branches are exempt since dependabot.yml controls their naming.
'use strict';

const { BRANCH_NAME_RE, BOT_LOGINS } = require('./constants');

module.exports = async ({ context, core }) => {
  const branch = context.payload.pull_request.head.ref;
  const author = context.payload.pull_request.user.login;

  if (BOT_LOGINS.includes(author) || branch.startsWith('dependabot/')) {
    core.info(`Skipping branch name check for bot branch "${branch}".`);
    return;
  }

  if (!BRANCH_NAME_RE.test(branch)) {
    core.setFailed(
      `Branch name "${branch}" is invalid. Expected "<type>/<short-description>" ` +
        'where <type> is one of feat|fix|refactor|perf|test|docs|build|ci|chore|revert|style ' +
        'and <short-description> uses only lowercase letters, digits, dots, underscores or dashes.'
    );
    return;
  }

  core.summary
    .addHeading('Branch name check', 3)
    .addRaw(`Branch "${branch}" is valid.`)
    .write();
};
