// Validates that every commit in the PR has a Conventional Commits header.
// Lists commits via the API with pagination to support PRs with many commits.
'use strict';

const { CONVENTIONAL_COMMIT_RE } = require('./constants');

module.exports = async ({ github, context, core }) => {
  const commits = await github.paginate(github.rest.pulls.listCommits, {
    owner: context.repo.owner,
    repo: context.repo.repo,
    pull_number: context.payload.pull_request.number,
    per_page: 100,
  });

  const offenders = [];
  for (const commit of commits) {
    const header = commit.commit.message.split('\n')[0];
    if (!CONVENTIONAL_COMMIT_RE.test(header)) {
      offenders.push(`${commit.sha.slice(0, 7)}: "${header}"`);
    }
  }

  if (offenders.length > 0) {
    core.setFailed(
      'The following commits do not follow Conventional Commits ' +
        '("<type>(<optional scope>)!: <description>"):\n' +
        offenders.map((line) => `- ${line}`).join('\n')
    );
    return;
  }

  core.summary
    .addHeading('Commit message check', 3)
    .addRaw(`All ${commits.length} commit(s) follow Conventional Commits.`)
    .write();
};
