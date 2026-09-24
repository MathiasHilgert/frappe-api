// Computes and applies "managed" labels (type/module/area/size/breaking-change)
// based on the PR title, body and changed files, then reconciles them:
// stale managed labels are removed, missing ones are added. Fails the check
// if no `type:` label results, since a type label is mandatory.
'use strict';

const { CONVENTIONAL_COMMIT_RE } = require('./constants');

const MANAGED_PREFIXES = ['type: ', 'module: ', 'area: ', 'size: '];
const BREAKING_LABEL = 'breaking-change';

function isManagedLabel(name) {
  return MANAGED_PREFIXES.some((prefix) => name.startsWith(prefix)) || name === BREAKING_LABEL;
}

function typeLabelFromTitle(title) {
  const match = title.match(CONVENTIONAL_COMMIT_RE);
  return match ? `type: ${match[1]}` : null;
}

function isBreaking(title, body) {
  return title.includes('!') || (body || '').includes('BREAKING CHANGE');
}

// Maps one changed file path to zero or more "area:"/"module:" labels.
function labelsForFile(filename) {
  const labels = [];

  if (filename.startsWith('.github/')) {
    labels.push('area: ci');
  }
  if (filename.startsWith('docs/') || filename.endsWith('.md')) {
    labels.push('area: docs');
  }
  if (filename === 'go.mod' || filename === 'go.sum') {
    labels.push('area: dependencies');
  }

  const internalMatch = filename.match(/^internal\/([^/]+)\//);
  if (internalMatch) {
    labels.push(`module: ${internalMatch[1]}`);
  }
  const cmdMatch = filename.match(/^cmd\/([^/]+)\//);
  if (cmdMatch) {
    labels.push(`module: cmd-${cmdMatch[1]}`);
  }

  return labels;
}

// Size buckets by total additions+deletions, excluding go.sum and generated
// files (best-effort: files under a "generated" path or with a
// "// Code generated" style name marker are still counted here since GitHub's
// file list has no generated-file flag; go.sum is the one reliable case).
function sizeLabel(files) {
  const changed = files
    .filter((file) => file.filename !== 'go.sum')
    .reduce((total, file) => total + file.additions + file.deletions, 0);

  if (changed < 10) return 'size: XS';
  if (changed < 100) return 'size: S';
  if (changed < 400) return 'size: M';
  if (changed < 1000) return 'size: L';
  return 'size: XL';
}

module.exports = async ({ github, context, core }) => {
  const pr = context.payload.pull_request;

  const files = await github.paginate(github.rest.pulls.listFiles, {
    owner: context.repo.owner,
    repo: context.repo.repo,
    pull_number: pr.number,
    per_page: 100,
  });

  const desired = new Set();

  const typeLabel = typeLabelFromTitle(pr.title);
  if (typeLabel) {
    desired.add(typeLabel);
  }

  if (isBreaking(pr.title, pr.body)) {
    desired.add(BREAKING_LABEL);
  }

  for (const file of files) {
    for (const label of labelsForFile(file.filename)) {
      desired.add(label);
    }
  }

  desired.add(sizeLabel(files));

  if (![...desired].some((label) => label.startsWith('type: '))) {
    core.setFailed(
      'PR title does not map to a valid "type:" label. ' +
        'Ensure the title follows Conventional Commits with a recognized type.'
    );
    return;
  }

  const current = pr.labels.map((label) => label.name);
  const currentManaged = current.filter(isManagedLabel);

  const toRemove = currentManaged.filter((label) => !desired.has(label));
  const toAdd = [...desired].filter((label) => !current.includes(label));

  for (const label of toRemove) {
    await github.rest.issues.removeLabel({
      owner: context.repo.owner,
      repo: context.repo.repo,
      issue_number: pr.number,
      name: label,
    });
  }

  if (toAdd.length > 0) {
    // addLabels auto-creates any label that does not exist yet.
    await github.rest.issues.addLabels({
      owner: context.repo.owner,
      repo: context.repo.repo,
      issue_number: pr.number,
      labels: toAdd,
    });
  }

  core.summary
    .addHeading('Label sync', 3)
    .addList([...desired])
    .write();
};
