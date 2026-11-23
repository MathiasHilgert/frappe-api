#!/usr/bin/env python3
"""PreToolUse hook of forge-writer: enforce the work economy instead of only asking for it.

Most of a writer's time is model generation, and whole-file
rewrites plus long build/test logs are the costliest turns. Two rules:

  write   Write on a file that already exists is denied: change it with Edit on the lines that change.
  rewrite The same through Bash, which is how writers actually edited (1968 Bash rewrites vs 19 Write calls over 232
          runs, because bypass mode tells agents to edit through Bash): a redirect (`>`, `tee`) or a Python
          open(..., 'w') onto a git-tracked file is denied. Appending (`>>`), `sed -i` line edits and new files pass.
  trim    `dotnet build|test|format` and `just` commands are rewritten to run through run_trimmed.sh, which keeps the
          exit code, prints only failures and the summary, and saves the full log to a file the writer can open.

Reads the hook JSON on stdin; prints a PreToolUse decision on stdout, or nothing to let the call through.
"""
import json
import os
import re
import shlex
import subprocess
import sys

TRIM = re.compile(r"(^|[\s;&|(])(dotnet\s+(build|test|format)|just\s+\w)")
# Targets of a whole-file overwrite inside a shell command: `> file` (not `>>`, `2>`, `>&`), `tee file`, and Python's
# open('file', 'w').
REDIRECT = re.compile(r"(?<![>&0-9])>(?![>&])\s*([^\s;&|<>()]+)")
TEE = re.compile(r"\btee\s+(?:-[a-z]+\s+)*([^\s;&|<>()]+)")
PY_OPEN = re.compile(r"open\(\s*['\"]([^'\"]+)['\"]\s*,\s*['\"]w")
RUNNER = os.path.join(os.path.dirname(os.path.abspath(__file__)), "run_trimmed.sh")


def decide(decision, reason, updated=None):
    out = {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": decision, "permissionDecisionReason": reason}}
    if updated is not None:
        out["hookSpecificOutput"]["updatedInput"] = updated
    print(json.dumps(out))


def tracked(path, cwd):
    """True when path is an existing file git tracks (scratch files, logs and new files are never blocked)."""
    path = path.strip("'\"")
    if path.startswith("/dev/") or not path:
        return False
    full = path if os.path.isabs(path) else os.path.join(cwd, path)
    if not os.path.isfile(full):
        return False
    directory = os.path.dirname(full) or "."
    probe = subprocess.run(["git", "-C", directory, "ls-files", "--error-unmatch", os.path.basename(full)], capture_output=True)
    return probe.returncode == 0


def main():
    event = json.load(sys.stdin)
    tool, data = event.get("tool_name"), event.get("tool_input") or {}
    if tool == "Write":
        path = data.get("file_path", "")
        if path and os.path.exists(path):
            decide("deny", f"{path} already exists: change it with Edit on the lines that change (several Edit calls in one turn if needed). Write is only for new files.")
        return
    if tool == "Bash":
        command = data.get("command", "")
        cwd = event.get("cwd") or os.getcwd()
        targets = REDIRECT.findall(command) + TEE.findall(command) + PY_OPEN.findall(command)
        rewritten = [t for t in targets if tracked(t, cwd)]
        if rewritten:
            decide("deny", f"This rewrites the whole tracked file {rewritten[0]}: every unchanged line costs output tokens and turns. Change only the lines that change with Edit (several Edit calls in one turn if needed), or `sed -i` for a one-line substitution.")
            return
        # A command that already filters its own output (a pipe) is left alone: wrapping it would trim the filter's
        # output again and, since the pipe hides the build's exit code, could cut away the errors it kept.
        if not TRIM.search(command) or "|" in command or RUNNER in command or "FORGE_NO_TRIM=1" in command:
            return
        trimmed = f"bash {shlex.quote(RUNNER)} {shlex.quote(command)}"
        decide("allow", "build/test output trimmed to failures and summary; the full log path is printed", {**data, "command": trimmed})


if __name__ == "__main__":
    main()
