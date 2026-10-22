#!/usr/bin/env bash
# Validate a branch name against the repository naming convention.
# Usage: scripts/git/check-branch.sh [branch]
# Default branch: $GITHUB_HEAD_REF when set, otherwise the current branch.
set -euo pipefail

TYPES='feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert'
SLUG='[a-z0-9]+(-[a-z0-9]+)*'
ISSUE='([0-9]+-|[a-z]+-[0-9]+-)?'
SEMVER='(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?'

PATTERNS=(
  '^main$'
  "^($TYPES)/$ISSUE$SLUG\$"
  '^dependabot/.+$'
  "^release/v$SEMVER\$"
)

branch="${1:-${GITHUB_HEAD_REF:-}}"
if [[ -z "$branch" ]]; then
  branch="$(git symbolic-ref --quiet --short HEAD 2>/dev/null || true)"
fi
if [[ -z "$branch" ]]; then
  echo "error: could not determine the branch (detached HEAD?). Pass it as an argument." >&2
  exit 2
fi

for pattern in "${PATTERNS[@]}"; do
  if [[ "$branch" =~ $pattern ]]; then
    echo "branch ok: $branch"
    exit 0
  fi
done

cat >&2 <<MSG
error: branch name '$branch' does not follow the convention.

Allowed:
  main
  <type>/<kebab-slug>            type: ${TYPES//|/, }
  <type>/<issue>-<kebab-slug>    issue: 123 or abc-123
  dependabot/**
  release/v<semver>

Examples:
  feat/table-reservations
  fix/123-null-order-total
  chore/abc-42-bump-sdk
  release/v1.4.0
MSG
exit 1
