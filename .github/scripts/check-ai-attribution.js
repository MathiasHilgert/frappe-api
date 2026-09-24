// Rejects AI attribution (Claude/Copilot/ChatGPT/etc. signatures, "Generated
// with ..." footers, robot emoji) from every PR commit message and from the
// PR body. HTML comments in the PR body are stripped first because the PR
// template itself mentions these words inside instructional comments.
'use strict';

const { AI_ATTRIBUTION_PATTERNS } = require('./constants');

function stripHtmlComments(text) {
  return text.replace(/<!--[\s\S]*?-->/g, '');
}

function findMatch(text) {
  for (const pattern of AI_ATTRIBUTION_PATTERNS) {
    const match = text.match(pattern);
    if (match) {
      return match[0];
    }
  }
  return null;
}

module.exports = async ({ github, context, core }) => {
  const violations = [];

  const commits = await github.paginate(github.rest.pulls.listCommits, {
    owner: context.repo.owner,
    repo: context.repo.repo,
    pull_number: context.payload.pull_request.number,
    per_page: 100,
  });

  for (const commit of commits) {
    const match = findMatch(commit.commit.message);
    if (match) {
      violations.push(`Commit ${commit.sha.slice(0, 7)} contains AI attribution: "${match}"`);
    }
  }

  const body = stripHtmlComments(context.payload.pull_request.body || '');
  const bodyMatch = findMatch(body);
  if (bodyMatch) {
    violations.push(`PR body contains AI attribution: "${bodyMatch}"`);
  }

  if (violations.length > 0) {
    core.setFailed(
      'AI attribution is not allowed in commits or the PR description. ' +
        'Remove it and force-push / edit the description:\n' +
        violations.map((line) => `- ${line}`).join('\n')
    );
    return;
  }

  core.summary.addHeading('AI attribution check', 3).addRaw('No AI attribution found.').write();
};
