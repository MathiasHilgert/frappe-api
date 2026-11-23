#!/usr/bin/env bash
# Usage: watch-main.sh <sha-prefix>. Waits for every non-Release main run of that commit; prints failures; never reports green without runs.
set -u
sha="$1"
mapfile -t ids < <(gh run list --branch main --limit 20 --json databaseId,headSha,name \
  --jq ".[] | select(.headSha|startswith(\"$sha\")) | select(.name!=\"Release\") | .databaseId")
if [ "${#ids[@]}" -eq 0 ]; then echo "NO RUNS FOUND for $sha"; exit 2; fi
fail=0
for id in "${ids[@]}"; do
  if ! gh run watch "$id" --exit-status --interval 30 >/dev/null 2>&1; then
    fail=1
    echo "FAILED: $(gh run view "$id" --json name --jq .name) ($id)"
    gh run view "$id" --json jobs --jq '.jobs[] | select(.conclusion=="failure") | "  job: \(.name)"'
  fi
done
[ "$fail" -eq 0 ] && echo "main $sha green (${#ids[@]} runs)"
exit "$fail"
