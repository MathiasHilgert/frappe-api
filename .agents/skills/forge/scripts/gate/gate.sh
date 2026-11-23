#!/usr/bin/env bash
# Usage (from the worktree root): gate.sh [--base <ref>] [--rerun]
# forge-writer's final gate in one call. Builds the solution once (errors only), then runs every independent check in
# parallel, one log per step: format of the changed C# files, typos on the changed files, line length (150), squawk on changed migrations,
# pending EF model changes of the touched modules, and every test app the change can break (affected.py). Prints one
# line per step and, for a failure, its first relevant lines and log path. Exit 0 only when every step passed.
#   --rerun  after fixing: rebuild, then rerun only the steps that failed last time plus the test apps affected by files
#            changed since then. Never rerun the whole gate by hand.
# State lives in the git dir (never tracked): <git-dir>/forge-gate/. Give the Bash call a 600000 ms timeout.
set -uo pipefail
base=origin/main
rerun=false
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base=$2; shift 2 ;;
    --rerun) rerun=true; shift ;;
    *) echo "unknown argument $1"; exit 64 ;;
  esac
done
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(git rev-parse --show-toplevel) || exit 64
cd "$root"
state="$(git rev-parse --absolute-git-dir)/forge-gate"
mkdir -p "$state"
stamp="$state/stamp"

mapfile -t changed < <({ git diff --name-only "$base"...HEAD; git ls-files -mo --exclude-standard; } | sort -u | while read -r f; do [ -e "$f" ] && echo "$f"; done)
mapfile -t cs < <(printf '%s\n' "${changed[@]}" | grep -E '\.cs$' || true)

echo "== build"
if ! dotnet build Frappe.slnx --no-restore --nologo -v q -clp:ErrorsOnly --tl:off >"$state/build.log" 2>&1; then
  grep -E 'error' "$state/build.log" | sed -E 's#^.*/(src|tests)/#\1/#' | sort -u | head -40
  echo "FAIL build (log: $state/build.log)"
  exit 1
fi
echo "PASS build"

# Every step of a full gate: name and command.
declare -A cmd
if [ ${#cs[@]} -gt 0 ]; then
  cmd[format]="dotnet format Frappe.slnx --verify-no-changes --no-restore --include $(printf '%q ' "${cs[@]}")"
fi
if [ -f eng/typos.toml ] && command -v uvx >/dev/null && [ ${#changed[@]} -gt 0 ]; then
  cmd[typos]="uvx --from typos==1.52.0 typos --config eng/typos.toml --force-exclude $(printf '%q ' "${changed[@]}")"
fi
if [ -f .github/scripts/line-length.mjs ] && command -v node >/dev/null; then
  cmd[line-length]="node .github/scripts/line-length.mjs"
fi
if printf '%s\n' "${changed[@]}" | grep -q '/Migrations/' && [ -x .github/scripts/migration-sql.sh ] && [ -f eng/squawk.toml ]; then
  cmd[squawk]="out=\$(mktemp -d) && .github/scripts/migration-sql.sh $(printf '%q' "$base") \"\$out\" && npx -y squawk-cli@2.72.0 --config eng/squawk.toml \"\$out\"/*.sql"
fi
# Pending EF model changes, only for modules the change touched.
while read -r factory; do
  [ -n "$factory" ] || continue
  module=$(echo "$factory" | sed -E 's#^(src/(Modules|Platform)/[^/]+)/.*#\1#')
  printf '%s\n' "${changed[@]}" | grep -q "^$module/" || continue
  project=$(dirname "$factory")
  while [ ! -f "$project/$(basename "$project").csproj" ] && [ "$project" != "src" ]; do project=$(dirname "$project"); done
  cmd["ef:$(basename "$project")"]="dotnet tool restore >/dev/null && dotnet ef migrations has-pending-model-changes --project $(printf '%q' "$project") --no-build"
done < <(grep -rl --include='*.cs' 'IDesignTimeDbContextFactory' src 2>/dev/null || true)
mapfile -t apps < <(python3 -I "$here/affected.py" "$root" "${changed[@]}")
for app in "${apps[@]}"; do
  [ -n "$app" ] && cmd["tests:$(basename "$app")"]="timeout 540 dotnet test --project $(printf '%q' "$app") --no-build --timeout 8m --maximum-failed-tests 10"
done

# --rerun: the steps that failed last time, plus the test apps affected by files changed since the last gate.
selected=("${!cmd[@]}")
if $rerun && [ -f "$stamp" ]; then
  mapfile -t since < <(printf '%s\n' "${changed[@]}" | while read -r f; do [ "$f" -nt "$stamp" ] && echo "$f"; done)
  mapfile -t again < <(python3 -I "$here/affected.py" "$root" "${since[@]}" | sed 's#.*/#tests:#')
  selected=()
  for step in "${!cmd[@]}"; do
    failed=false; [ -f "$state/$step.rc" ] && [ "$(cat "$state/$step.rc")" != "0" ] && failed=true
    touched=false; printf '%s\n' "${again[@]}" | grep -qx "$step" && touched=true
    if $failed || $touched; then selected+=("$step"); else echo "SKIP $step (passed last time, inputs unchanged)"; fi
  done
fi
touch "$stamp"

# Several test apps at once must not share one reused container (just test warns about it).
tests_selected=$(printf '%s\n' "${selected[@]}" | grep -c '^tests:' || true)
for step in "${selected[@]}"; do
  log="$state/$step.log"
  (
    if [ "$tests_selected" -gt 1 ]; then unset FRAPPE_TEST_REUSE; fi
    bash -c "${cmd[$step]}" >"$log" 2>&1
    echo $? >"$state/$step.rc"
  ) &
done
wait

status=0
for step in $(printf '%s\n' "${selected[@]}" | sort); do
  rc=$(cat "$state/$step.rc")
  if [ "$rc" = "0" ]; then
    echo "PASS $step"
  else
    status=1
    echo "FAIL $step (exit $rc, log: $state/$step.log)"
    grep -E -i 'error|fail|exception|assert|expected|but was|warn(ing)? [A-Z]+[0-9]+|  at ' "$state/$step.log" | head -25 | sed 's/^/  /'
  fi
done
[ "$status" -eq 0 ] && echo "gate: all green" || echo "gate: fix the failures, then run gate.sh --rerun"
exit "$status"
