#!/usr/bin/env bash
# Usage: merge-queue.sh <pr>... — merges the PRs in order with the fewest CI waits.
# For each PR: wait for its own checks, fail on any non-green check, squash-merge: the commit takes the PR title and the PR body (repo settings PR_TITLE/PR_BODY), so `git log` keeps the why. The next PR is merged
# right away when it touches none of the modules (src/Modules/<M>, src/Platform, deploy, .github) the previous merges touched
# since the last main watch and GitHub reports it MERGEABLE; otherwise main's CI is watched first (watch-main.sh) and, when the
# PR is BEHIND/CONFLICTING, the queue stops so the orchestrator rebases it. Main's CI is always watched once at the end.
# A PR that commits a schema dump (schema.sql) is rebased on GitHub first whenever main changed any dump since its merge base:
# the migrations job compares main upgraded by the PR with the PR's committed dumps, so a sibling's merged migration fails it
# with no textual conflict.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../../../.." && pwd)"
cd "$repo"
areas() { gh pr diff "$1" --name-only | sed -E 's#^(src/Modules/[^/]+|src/Platform|tests/Modules/[^/]+|deploy|\.github|contracts|tools/bruno)/.*#\1#' | sed -E 's#^tests/Modules/#src/Modules/#' | sort -u; }
watch_main() { local m; git fetch -q origin; m=$(git rev-parse --short origin/main)
  for _ in $(seq 1 20); do [ "$(gh run list --branch main --limit 10 --json headSha --jq "[.[]|select(.headSha|startswith(\"$m\"))]|length")" -gt 0 ] && break; sleep 15; done
  sleep 20; bash "$here/watch-main.sh" "$m"; }
touched=""
for pr in "$@"; do
  mine=$(areas "$pr")
  if [ -n "$touched" ] && comm -12 <(echo "$touched") <(echo "$mine") | grep -q .; then
    echo "#$pr shares $(comm -12 <(echo "$touched") <(echo "$mine") | tr '\n' ' ')with earlier merges: watching main first"
    watch_main || { echo "MAIN RED: stopping before #$pr"; exit 1; }
    touched=""
  fi
  if gh pr diff "$pr" --name-only | grep -q 'schema\.sql$'; then
    git fetch -q origin main "pull/$pr/head"
    if git diff --name-only "$(git merge-base origin/main FETCH_HEAD)" origin/main | grep -q 'schema\.sql$'; then
      echo "#$pr carries schema dumps and main changed a dump since its base: rebasing it"
      gh pr update-branch "$pr" --rebase || { echo "#$pr could not be rebased: rebase it by hand"; exit 2; }
      sleep 30
    fi
  fi
  until [ "$(gh pr checks "$pr" --json state --jq '[.[]|select(.state=="IN_PROGRESS" or .state=="PENDING" or .state=="QUEUED")]|length')" = "0" ]; do sleep 30; done
  bad=$(gh pr checks "$pr" --json state,name --jq '.[]|select(.state!="SUCCESS" and .state!="SKIPPED" and .state!="NEUTRAL")|"\(.state) \(.name)"')
  [ -n "$bad" ] && { echo "#$pr CHECKS FAILED:"; echo "$bad"; exit 1; }
  state=$(gh pr view "$pr" --json mergeStateStatus --jq .mergeStateStatus)
  case "$state" in BEHIND|DIRTY) echo "#$pr is $state: rebase it, then rerun the queue from it"; exit 2 ;; esac
  title=$(gh pr view "$pr" --json title --jq .title)
  gh pr merge "$pr" --squash --delete-branch || { echo "MERGE FAILED #$pr"; exit 1; }
  echo "merged #$pr"
  touched=$(printf '%s\n%s\n' "$touched" "$mine" | sed '/^$/d' | sort -u)
done
watch_main
