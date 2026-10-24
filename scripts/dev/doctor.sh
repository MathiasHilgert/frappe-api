#!/usr/bin/env bash
# Reports every missing or outdated prerequisite for local development, then exits non-zero if any is missing.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
missing=0
ok() { printf 'ok    %-16s %s\n' "$1" "$2"; }
bad() { printf 'MISS  %-16s %s\n' "$1" "$2"; missing=$((missing + 1)); }

# The SDK global.json pins (rollForward latestFeature: same major.minor, feature band at least the pinned one).
want="$(sed -n 's/.*"version": *"\([0-9.]*\)".*/\1/p' "$root/global.json" | head -n1)"
if command -v dotnet >/dev/null 2>&1; then
  if (cd "$root" && dotnet --version >/dev/null 2>&1); then
    ok dotnet "$(cd "$root" && dotnet --version) (global.json pins $want)"
  else
    bad dotnet "no installed SDK satisfies global.json ($want); install it from https://dot.net"
  fi
else
  bad dotnet "not found; install the .NET SDK $want from https://dot.net"
fi

if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then ok docker "$(docker --version)"; else bad docker "installed but the daemon is not reachable"; fi
  if docker compose version >/dev/null 2>&1; then ok "docker compose" "$(docker compose version --short)"; else bad "docker compose" "plugin missing"; fi
else
  bad docker "not found; the local stack and integration tests need it"
fi

for tool in just git curl jq; do
  if command -v "$tool" >/dev/null 2>&1; then ok "$tool" "$(command -v "$tool")"; else bad "$tool" "not found"; fi
done

if [[ "$(git -C "$root" config core.hooksPath 2>/dev/null)" == scripts/git/hooks ]]; then
  ok "git hooks" "installed"
else
  bad "git hooks" "not installed; run 'just setup'"
fi

if (( missing > 0 )); then echo "$missing prerequisite(s) missing; see docs/troubleshooting.md." >&2; exit 1; fi
echo "All prerequisites present. Next: just dev"
