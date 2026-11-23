#!/usr/bin/env bash
# Runs one shell command for forge-writer and prints only what the writer needs: on success the last lines (the
# summary), on failure the error and failed-test lines plus the tail. The exit code is the command's. The full log
# stays in a temp file whose path is printed, so nothing is lost. Used by writer_guard.py; never call it by hand.
set -u
log=$(mktemp "${TMPDIR:-/tmp}/forge-run.XXXXXX.log")
# First line, before running: a command killed by the Bash timeout still leaves the path of its partial log.
echo "[full log: $log]"
bash -c "$1" >"$log" 2>&1
rc=$?
lines=$(wc -l <"$log")
if [ "$rc" -eq 0 ]; then
  tail -n 15 "$log"
else
  grep -E -i -m 120 'error|fail|exception|assert|expected|but was|warn(ing)? [A-Z]+[0-9]+|  at ' "$log" || true
  echo '--- tail ---'
  tail -n 30 "$log"
fi
echo "[exit $rc · $lines lines]"
exit "$rc"
