#!/usr/bin/env bash
# Lint every commit in <base-ref>..<head-ref> with commitlint (config-conventional).
# Runs the pinned official commitlint image. Messages are read on the host and piped in,
# so the container never touches the repository (worktrees and submodules just work).
# Usage: scripts/git/lint-commits.sh <base-ref> <head-ref>
set -euo pipefail

COMMITLINT_IMAGE="${COMMITLINT_IMAGE:-commitlint/commitlint:19.8.1}"

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <base-ref> <head-ref>" >&2
  exit 2
fi
base="$1"
head="$2"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
config="${COMMITLINT_CONFIG:-$(cd "$script_dir/../.." && pwd)/.config/commitlint.config.mjs}"

git rev-parse --verify --quiet "$base^{commit}" >/dev/null || { echo "error: unknown base ref '$base'" >&2; exit 2; }
git rev-parse --verify --quiet "$head^{commit}" >/dev/null || { echo "error: unknown head ref '$head'" >&2; exit 2; }

count="$(git rev-list --count "$base..$head")"
echo "commitlint: checking $count commit(s) in $base..$head"
[[ "$count" -eq 0 ]] && exit 0

failed=0
while read -r sha; do
  echo "--- ${sha:0:12} $(git log -1 --format=%s "$sha")"
  if ! git log -1 --format=%B "$sha" | docker run --rm -i \
      -v "$config:/etc/commitlint/commitlint.config.mjs:ro" \
      "$COMMITLINT_IMAGE" --config /etc/commitlint/commitlint.config.mjs; then
    failed=$((failed + 1))
  fi
done < <(git rev-list --reverse "$base..$head")

if [[ $failed -gt 0 ]]; then
  echo "commitlint: $failed of $count commit(s) failed" >&2
  exit 1
fi
echo "commitlint: all $count commit(s) passed"
