// Assigns the PR to its author so ownership is obvious at a glance. Skips
// bot authors (e.g. dependabot) since they cannot meaningfully own follow-up.
'use strict';

const { BOT_LOGINS } = require('./constants');

module.exports = async ({ github, context, core }) => {
  const pr = context.payload.pull_request;
  const author = pr.user.login;

  if (BOT_LOGINS.includes(author)) {
    core.info(`Skipping assignment for bot author "${author}".`);
    return;
  }

  const alreadyAssigned = pr.assignees.some((assignee) => assignee.login === author);
  if (alreadyAssigned) {
    core.info(`"${author}" is already assigned.`);
    return;
  }

  await github.rest.issues.addAssignees({
    owner: context.repo.owner,
    repo: context.repo.repo,
    issue_number: pr.number,
    assignees: [author],
  });
  core.info(`Assigned "${author}" to PR #${pr.number}.`);
};
