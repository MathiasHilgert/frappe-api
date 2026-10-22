#!/usr/bin/env bash
# Lint every commit in <base-ref>..<head-ref> with commitlint (config-conventional).
# Runs the pinned official commitlint image; the repository is mounted read-only.
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
repo="$(git rev-parse --show-toplevel)"

git rev-parse --verify --quiet "$base^{commit}" >/dev/null || { echo "error: unknown base ref '$base'" >&2; exit 2; }
git rev-parse --verify --quiet "$head^{commit}" >/dev/null || { echo "error: unknown head ref '$head'" >&2; exit 2; }

count="$(git rev-list --count "$base..$head")"
echo "commitlint: checking $count commit(s) in $base..$head"
[[ "$count" -eq 0 ]] && exit 0

# Resolve to SHAs on the host so the container needs no ref lookups beyond the object store.
base_sha="$(git rev-parse "$base^{commit}")"
head_sha="$(git rev-parse "$head^{commit}")"

exec docker run --rm \
  --user "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0='*' \
  -v "$repo:/repo:ro" \
  -v "$config:/etc/commitlint/commitlint.config.mjs:ro" \
  -w /repo \
  "$COMMITLINT_IMAGE" \
  --config /etc/commitlint/commitlint.config.mjs \
  --from "$base_sha" --to "$head_sha" --verbose
