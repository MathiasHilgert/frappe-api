#!/usr/bin/env bash
# Usage (from the worktree root): test-class.sh <TestClass> [<TestMethod>] [--no-build]
# The inner-loop test run of forge-writer: finds the test project that holds the class, builds only that project
# (incremental, errors only), and runs only that class (or one test) with a timeout under the 120 s Bash default.
# A filter that matches no test fails (exit 9) instead of passing empty. Output: the summary, plus failures when any;
# the full log path is printed first. Exit code: the test run's (MTP codes: 2 failed tests, 3 timeout, 9 none ran).
set -uo pipefail
cls=${1:?test class name}
method=${2:-}
nobuild=false
for a in "$@"; do [ "$a" = "--no-build" ] && nobuild=true; done
[ "$method" = "--no-build" ] && method=""
file=$(grep -rlE "class[[:space:]]+${cls}\b" tests --include='*.cs' 2>/dev/null | head -1)
[ -n "$file" ] || { echo "no class ${cls} under tests/"; exit 9; }
proj=$(dirname "$file")
while [ ! -f "$proj/$(basename "$proj").csproj" ]; do
  [ "$proj" = "tests" ] || [ "$proj" = "." ] && { echo "no test project for $file"; exit 9; }
  proj=$(dirname "$proj")
done
log=$(mktemp "${TMPDIR:-/tmp}/forge-test.XXXXXX.log")
echo "[project $proj · full log: $log]"
if ! $nobuild; then
  if ! dotnet build "$proj" --no-restore --nologo -v q -clp:ErrorsOnly --tl:off >"$log" 2>&1; then
    grep -E 'error' "$log" | sort -u | head -40
    echo "[build failed]"
    exit 1
  fi
fi
filter="/*/*/${cls}/${method:-*}"
# Integration classes start containers: keep FRAPPE_TEST_REUSE as the developer set it (one session per worktree).
timeout 110 dotnet test --project "$proj" --no-build --timeout 100s --maximum-failed-tests 10 \
  --minimum-expected-tests 1 --treenode-filter "$filter" >>"$log" 2>&1
rc=$?
if [ "$rc" -eq 0 ]; then
  grep -E -i '^\s*(total|error|correcto|succeeded|passed|failed|omitido|skipped|duraci|duration)' "$log" | tail -6
else
  grep -E -i 'failed|error|exception|assert|expected|but was|  at |exit code|c[oó]digo de salida' "$log" | head -60
  [ "$rc" -eq 124 ] && echo "[killed at 110 s: the class hangs or its containers are slow; read the log]"
fi
echo "[exit $rc]"
exit "$rc"
