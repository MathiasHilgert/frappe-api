#!/usr/bin/env bash
# Usage: eng/pr-check.sh [base]  (just pr-check) — the CI checks a branch most often fails after a push, run locally
# before pushing: commit and branch conventions, Bruno seq numbers, justfile format, build, format of the changed files,
# pending EF model changes, the committed contracts and, when a migration changed, the committed schema dumps. A step whose
# subject does not exist yet prints a skip. Base defaults to origin/main.
set -euo pipefail
base=${1:-origin/main}
cd "$(git rev-parse --show-toplevel)"
step() { printf '\n== %s\n' "$1"; }
skip() { echo "skip: $1"; }
changed=$(git diff --name-only "$base"...HEAD; git ls-files -mo --exclude-standard)

step "Commits and branch follow the conventions"
if [ -f .github/scripts/conventions.mjs ]; then
  node --input-type=module -e '
import { checkCommitMessage, checkBranch } from "./.github/scripts/conventions.mjs";
import { execSync } from "node:child_process";
const base = process.argv[1];
const shas = execSync(`git rev-list ${base}..HEAD`).toString().trim().split("\n").filter(Boolean);
let bad = 0;
for (const sha of shas) {
  const message = execSync(`git log -1 --format=%B ${sha}`).toString();
  const problems = [checkCommitMessage(message)].flat().filter(Boolean);
  if (problems.length) { bad++; console.error(`${sha.slice(0, 7)}: ${problems.join("; ")}`); }
}
const branch = execSync("git branch --show-current").toString().trim();
const first = shas.length ? execSync(`git log -1 --format=%s ${shas[shas.length - 1]}`).toString() : "";
const type = (first.match(/^([a-z]+)/) || [])[1];
const branchProblems = [type ? checkBranch(branch, type) : null].flat().filter(Boolean);
if (branchProblems.length) { bad++; console.error(`branch ${branch}: ${branchProblems.join("; ")}`); }
if (bad) process.exit(1);
console.log(`${shas.length} commit(s) and branch ${branch} ok`);
' "$base"
else
  skip "no convention scripts yet (.github/scripts/conventions.mjs)"
fi

step "Bruno seq numbers are unique per folder"
if [ -d tools/bruno ]; then
  dupes=$(for d in tools/bruno/*/; do find "$d" -maxdepth 1 -name '*.yml' ! -name folder.yml -exec grep -h -E '^  seq: [0-9]+' {} + 2>/dev/null | sort | uniq -d | sed "s#^ *#$d #"; done)
  [ -z "$dupes" ] || { echo "duplicate seq: $dupes"; exit 1; }
  echo ok
else
  skip "no Bruno collection yet (tools/bruno)"
fi

step "Justfiles are formatted"
for file in .justfile eng/just/*.just; do just --unstable --fmt --check --justfile "$file" > /dev/null; done
echo ok

step "Build"
dotnet build Frappe.slnx --nologo -v q

cs=$(printf '%s\n' "$changed" | grep -E '\.cs$' | sort -u | while read -r f; do [ -f "$f" ] && echo "$f"; done || true)
if [ -n "$cs" ]; then
  step "Format of the changed C# files (whitespace, style, analyzers)"
  mapfile -t files <<< "$cs"
  dotnet format Frappe.slnx --verify-no-changes --no-restore --include "${files[@]}"
fi

step "Every model change has its migration"
factories=$( [ -d src ] && grep -rl --include='*.cs' 'IDesignTimeDbContextFactory' src || true )
if [ -n "$factories" ]; then
  dotnet tool restore > /dev/null
  while read -r factory; do
    project=$(dirname "$factory")
    while [ ! -f "$project/$(basename "$project").csproj" ]; do
      case "$project" in
        . | /) echo "no project found for $factory"; exit 1 ;;
      esac
      project=$(dirname "$project")
    done
    dotnet ef migrations has-pending-model-changes --project "$project" --no-build > /dev/null
  done <<< "$factories"
  echo ok
else
  skip "no DbContext design-time factory yet"
fi

step "The committed contracts are current"
if [ -d contracts ]; then
  just contracts update > /dev/null
  git diff --exit-code -- contracts
else
  skip "no committed contracts yet"
fi

if printf '%s\n' "$changed" | grep -qE '/Migrations/|schema\.sql$|Directory\.Packages\.props$'; then
  step "The committed schema dumps are current"
  if [ -f deploy/compose.yaml ]; then
    just db schema > /dev/null
    git diff --exit-code -- ':(glob)**/schema.sql'
    untracked=$(git status --porcelain -- ':(glob)**/schema.sql' | grep '^??' || true)
    [ -z "$untracked" ] || { echo "untracked dumps: $untracked"; exit 1; }
  else
    skip "no local stack yet (deploy/compose.yaml)"
  fi
fi

printf '\npr-check: all green\n'
