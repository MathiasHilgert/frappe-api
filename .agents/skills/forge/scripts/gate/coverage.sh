#!/usr/bin/env bash
# Usage (from the worktree root): coverage.sh [<base-ref>] [<label>]
# Mirrors CI's changed-line coverage: builds the solution, runs every test project with coverage and fails (diff-cover) under 80% on the lines
# changed against <base-ref> (default origin/main). Slow (it runs every test project): use it once before committing, never in the inner loop.
set -uo pipefail
base=${1:-origin/main}; label=${2:-coverage}
root=$(git rev-parse --show-toplevel) || exit 64
cd "$root"
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT
dotnet build Frappe.slnx -v q >/dev/null 2>&1 || { echo "$label: BUILD FAILED"; exit 1; }
for p in $(git ls-files 'tests/*Tests.csproj'); do
  dotnet test --project "$p" --no-build --results-directory-layout per-module --results-directory "$out" -- \
    --coverage --coverage-output-format cobertura --coverage-output "$(basename "$p" .csproj).cobertura.xml" >/dev/null 2>&1 \
    || echo "$label: TESTS FAILED in $p"
done
mapfile -t reports < <(find "$out" -name '*.cobertura.xml')
[ ${#reports[@]} -gt 0 ] || { echo "$label: no coverage report produced"; exit 1; }
uvx --from diff-cover==9.2.0 diff-cover "${reports[@]}" --compare-branch "$base" --exclude "tests/*" "*/Migrations/*" "tools/**" --fail-under 80 2>&1 \
  | grep -E "^Coverage:|^Total|^Missing|%\): Missing" | sed "s|^|$label: |" | head -40
exit "${PIPESTATUS[0]}"
