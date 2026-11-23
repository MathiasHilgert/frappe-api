#!/usr/bin/env bash
# Run: bash .agents/skills/forge/scripts/hooks/writer_guard.test.sh — checks the writer hook's decisions on a throwaway
# git repo (a tracked file, a new file) without touching any worktree.
set -u
hook="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/writer_guard.py"
repo=$(mktemp -d)
trap 'rm -rf "$repo"' EXIT
git -C "$repo" init -q && mkdir -p "$repo/src" && echo old > "$repo/src/A.cs" && git -C "$repo" add -A && git -C "$repo" -c user.email=t@t -c user.name=t commit -qm base
fails=0
decision() {  # tool, json input -> deny | allow | pass
  python3 -I -c 'import json,sys; print(json.dumps({"tool_name": sys.argv[1], "cwd": sys.argv[2], "tool_input": json.loads(sys.argv[3])}))' "$1" "$repo" "$2" \
    | python3 -I "$hook" | python3 -I -c 'import json,sys; d=sys.stdin.read().strip(); print(json.loads(d)["hookSpecificOutput"]["permissionDecision"] if d else "pass")'
}
check() {  # name, expected, tool, input
  got=$(decision "$3" "$4")
  if [ "$got" = "$2" ]; then echo "PASS $1"; else echo "FAIL $1: expected $2, got $got"; fails=$((fails + 1)); fi
}
cmd() { python3 -I -c 'import json,sys; print(json.dumps({"command": sys.argv[1]}))' "$1"; }
check "Write on an existing file is denied" deny Write "{\"file_path\": \"$repo/src/A.cs\", \"content\": \"x\"}"
check "Write of a new file passes" pass Write "{\"file_path\": \"$repo/src/B.cs\", \"content\": \"x\"}"
check "heredoc onto a tracked file is denied" deny Bash "$(cmd "cat > src/A.cs <<'EOF'
x
EOF")"
check "python open(w) onto a tracked file is denied" deny Bash "$(cmd "python3 -c \"open('src/A.cs','w').write('x')\"")"
check "tee onto a tracked file is denied" deny Bash "$(cmd "echo x | tee src/A.cs")"
check "append passes" pass Bash "$(cmd "echo x >> src/A.cs")"
check "sed -i passes" pass Bash "$(cmd "sed -i 's/old/new/' src/A.cs")"
check "heredoc onto a new file passes" pass Bash "$(cmd "cat > src/C.cs <<'EOF'
x
EOF")"
check "dotnet build is wrapped" allow Bash "$(cmd "dotnet build Frappe.slnx")"
check "a piped dotnet command is left alone" pass Bash "$(cmd "dotnet test 2>&1 | tail -5")"
check "FORGE_NO_TRIM opts out" pass Bash "$(cmd "FORGE_NO_TRIM=1 dotnet build")"
check "plain git passes" pass Bash "$(cmd "git status")"
exit "$fails"
