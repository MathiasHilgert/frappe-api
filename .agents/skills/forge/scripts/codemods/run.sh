#!/usr/bin/env bash
# Deterministic codemods the orchestrator runs in a worktree BEFORE a writer starts, so the writer spends its
# turns only on what needs judgment. Usage: run.sh <worktree> <folder>...   (e.g. src/Modules/Geo tests/Modules/Geo)
# Each fix is restricted to explicit diagnostics: never a blanket --severity info pass (IDE0005 deletes usings).
set -euo pipefail
wt=$1; shift
cd "$wt"
mapfile -t projects < <(for d in "$@"; do find "$d" -name '*.csproj'; done | sort -u)
[ ${#projects[@]} -gt 0 ] || { echo "no projects under: $*"; exit 1; }
style=(IDE0028 IDE0090 IDE0161 IDE0300 IDE0301 IDE0302 IDE0303 IDE0304 IDE0305)  # collection expressions, target-typed new, file-scoped namespaces
analyzers=(CA1852)                                                                          # seal types nothing derives from
log=$(mktemp -d)
for p in "${projects[@]}"; do
  ( dotnet format style "$p" --no-restore --severity info --diagnostics "${style[@]}" >"$log/$(basename "$p").style" 2>&1
    dotnet format analyzers "$p" --no-restore --severity info --diagnostics "${analyzers[@]}" >"$log/$(basename "$p").an" 2>&1 ) &
done
wait
dotnet build Frappe.slnx --no-restore -v q 2>&1 | tail -2
echo "changed files: $(git status --porcelain | wc -l)   logs: $log"
