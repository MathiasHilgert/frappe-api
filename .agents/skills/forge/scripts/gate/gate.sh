#!/usr/bin/env bash
# Usage (from the worktree root): gate.sh [--base <ref>] [--rerun] [--coverage]
# forge-writer's final gate in one call. Builds the solution once (errors only), then runs every independent check in
# parallel, one log per step: format of the changed C# files, typos on the changed files, line length (150), Markdown lint of the changed
# .md files (the CI's markdownlint-cli2 version and config), the branch's commit messages against the repository conventions, squawk on
# changed migrations,
# pending EF model changes of the touched modules, and every test app the change can break (affected.py). Prints one
# line per step and, for a failure, its first relevant lines and log path. Exit 0 only when every step passed.
#   --coverage  also check changed-line coverage (CI's 80% bar): the affected test apps collect coverage in their one run, then
#            diff-cover reads the reports. Use it once before the final commit when production code changed.
#   The end-to-end tests (they build the Docker image) run only when deploy/, src/Hosts/ or the shared build files changed; CI runs them always.
#   --rerun  after fixing: rebuild, then rerun only the steps that failed last time plus the test apps affected by files
#            changed since then. Never rerun the whole gate by hand.
# State lives in the git dir (never tracked): <git-dir>/forge-gate/. Give the Bash call a 600000 ms timeout.
set -uo pipefail
base=origin/main
rerun=false
coverage=false
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base=$2; shift 2 ;;
    --rerun) rerun=true; shift ;;
    --coverage) coverage=true; shift ;;
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

# Several gates (parallel chains) share one machine: each takes cores / active gates for MSBuild nodes and TUnit parallelism,
# so they do not fight for the CPU. A gate registers its pid; entries of dead gates are dropped.
gates="${XDG_RUNTIME_DIR:-/tmp}/forge-gates"
mkdir -p "$gates" && touch "$gates/$$" && trap 'rm -f "$gates/$$"' EXIT
for g in "$gates"/*; do kill -0 "${g##*/}" 2>/dev/null || rm -f "$g"; done
active=$(find "$gates" -type f | wc -l)
share=$(( $(nproc) / (active > 0 ? active : 1) )); [ "$share" -lt 1 ] && share=1
export TUNIT_MAX_PARALLEL_TESTS="$share"
echo "== build ($active gate(s) running; $share cores each)"
if ! dotnet build Frappe.slnx --no-restore --nologo -v q -clp:ErrorsOnly --tl:off -m:"$share" >"$state/build.log" 2>&1; then
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
mapfile -t md < <(printf '%s\n' "${changed[@]}" | grep -E '\.md$' || true)
if [ ${#md[@]} -gt 0 ] && [ -f eng/markdownlint-cli2.jsonc ] && command -v npx >/dev/null; then
  # markdownlint-cli2 reads a config only from a file named *.markdownlint-cli2.jsonc; 0.24.0 is the version the CI action pins.
  cmd[markdown]="cfg=\$(mktemp -d)/eng.markdownlint-cli2.jsonc && cp eng/markdownlint-cli2.jsonc \"\$cfg\" && npx -y markdownlint-cli2@0.24.0 --config \"\$cfg\" $(printf '%q ' "${md[@]}")"
fi
if [ -f .github/scripts/conventions.mjs ] && command -v node >/dev/null; then
  cmd[commits]="node $(printf '%q' "$here/commits.mjs") $(printf '%q' "$base")"
fi
if printf '%s\n' "${changed[@]}" | grep -q '/Migrations/' && [ -x .github/scripts/migration-sql.sh ] && [ -f eng/squawk.toml ]; then
  # migration-sql.sh reads the base as origin/<ref> (CI passes the branch name): point a scratch remote ref at the gate's base, so a
  # slice gated against its parent commit or against origin/main sees only its own migrations as new.
  git update-ref refs/remotes/origin/forge-gate-base "$(git rev-parse "$base")"
  cmd[squawk]="out=\$(mktemp -d) && .github/scripts/migration-sql.sh forge-gate-base \"\$out\" && { ls \"\$out\"/*.sql >/dev/null 2>&1 || exit 0; } && npx -y squawk-cli@2.72.0 --config eng/squawk.toml \"\$out\"/*.sql"
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
# The end-to-end tests build the Docker image (about 2.5 minutes). CI always runs them; the gate runs them only when what the image
# packs or how it is deployed changed.
if ! printf '%s\n' "${changed[@]}" | grep -qE '^deploy/|^src/Hosts/|Dockerfile|^Directory\.(Build|Packages)\.props$|^global\.json$'; then
  mapfile -t apps < <(printf '%s\n' "${apps[@]}" | grep -v 'Frappe.EndToEnd.Tests$')
fi
# --coverage: the test steps collect coverage themselves, so no test app runs twice; diff-cover reads their reports after the wait.
covdir="$state/coverage"
$coverage && mkdir -p "$covdir"
for app in "${apps[@]}"; do
  [ -n "$app" ] || continue
  name=$(basename "$app")
  extra=""
  $coverage && extra=" --results-directory-layout per-module --results-directory $(printf '%q' "$covdir") -- --coverage --coverage-output-format cobertura --coverage-output $name.cobertura.xml"
  cmd["tests:$name"]="timeout 540 dotnet test --project $(printf '%q' "$app") --no-build --timeout 8m --maximum-failed-tests 10$extra"
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

# Several test apps at once keep FRAPPE_TEST_REUSE: a reused container is keyed by worktree + test app + fixture
# (TestReuse.KeyOf), so two apps never share one. The coverage step runs the same apps again at the same time, so it
# runs with reuse off (a second session of one app would reset the container under the first).
for step in "${selected[@]}"; do
  log="$state/$step.log"
  (
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
if $coverage && [ "$status" -eq 0 ]; then
  mapfile -t reports < <(find "$covdir" -name '*.cobertura.xml')
  if [ ${#reports[@]} -eq 0 ]; then
    status=1; echo "FAIL coverage (no report produced)"
  elif uvx --from diff-cover==9.2.0 diff-cover "${reports[@]}" --compare-branch "$base" --exclude "tests/*" "*/Migrations/*" "tools/**" \
      --fail-under 80 >"$state/coverage.log" 2>&1; then
    echo "PASS coverage ($(grep -E '^Coverage:' "$state/coverage.log" | head -1))"
  else
    status=1; echo "FAIL coverage (changed lines under 80%, log: $state/coverage.log)"
    grep -E "^Coverage:|^Total|%\): Missing" "$state/coverage.log" | head -20 | sed 's/^/  /'
  fi
fi
[ "$status" -eq 0 ] && echo "gate: all green" || echo "gate: fix the failures, then run gate.sh --rerun"
exit "$status"
