#!/usr/bin/env bash
# Compares a committed contract (OpenAPI document) with its version on the base branch. A breaking change passes only
# when the pull request title marks it with "!" (feat(geo)!: ...), so it reaches the changelog as one.
# Usage: breaking-changes.sh <contract path> <base branch>; reads TITLE from the environment.
set -euo pipefail

contract=$1
base_ref=$2
summary=${GITHUB_STEP_SUMMARY:-/dev/stdout}
base=$(mktemp)

# The contract on the base branch: at the same path, or at the path this branch moved it from (git's rename
# detection), so moving a contract still compares it with its previous version instead of skipping the check.
base_path=$contract
if ! git cat-file -e "origin/$base_ref:$contract" 2> /dev/null; then
  moved_from=$(git diff --find-renames --name-status "origin/$base_ref...HEAD" \
    | awk -F '\t' -v to="$contract" '$1 ~ /^R/ && $3 == to { print $2 }')
  base_path=${moved_from:-$contract}
fi

echo "### $contract" >> "$summary"
if [ "$base_path" != "$contract" ]; then
  echo "Moved from \`$base_path\` on \`$base_ref\`." >> "$summary"
fi
if ! git show "origin/$base_ref:$base_path" > "$base" 2> /dev/null; then
  echo "Not on \`$base_ref\` yet: nothing to compare." >> "$summary"
  exit 0
fi

set +e
report=$(oasdiff breaking "$base" "$contract" --fail-on ERR 2>&1)
status=$?
set -e

{
  echo '```'
  echo "${report:-No breaking changes.}"
  echo '```'
} >> "$summary"

if [ "$status" -eq 0 ]; then
  exit 0
fi

if [ "$status" -ne 1 ]; then
  echo "::error::oasdiff could not compare $contract (exit $status): $report"
  exit "$status"
fi

if [[ "${TITLE:-}" =~ ^[a-z]+\([a-z0-9-]+\)\!:\  ]]; then
  echo "::notice::Breaking changes in $contract, marked with ! in the title"
  exit 0
fi

echo "::error::Breaking changes in $contract: mark the title with ! (for example feat(geo)!: ...) and explain them, or keep the contract compatible"
exit 1
