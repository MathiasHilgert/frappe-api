#!/usr/bin/env bash
# Block AI attribution in commit messages, PR titles and PR bodies.
# Usage:
#   scripts/git/check-ai-attribution.sh commits <base-ref> <head-ref>
#   scripts/git/check-ai-attribution.sh text <file|->
# Only trailers and attribution phrases are matched; plain mentions (e.g. "bump openai") pass.
set -euo pipefail

AI_TOOLS='claude|anthropic|copilot|cursor|chatgpt|openai|gemini|codeium|devin|windsurf|aider|amazon[ -]?q|codewhisperer'
BOT_EMAILS='noreply@anthropic\.com|@anthropic\.com|copilot@github\.com|@openai\.com|@cursor\.(com|sh)|@codeium\.com|@windsurf\.com|@cognition\.ai|@aider\.chat'

# Extended regexes, matched case-insensitively against each line. Extend here.
PATTERNS=(
  "^[[:space:]]*co-authored-by:.*($AI_TOOLS)"
  "^[[:space:]]*co-authored-by:.*($BOT_EMAILS)"
  "generated (with|by|using) \[?($AI_TOOLS)"
  "🤖[[:space:]]*generated"
  "ai-generated"
)

regex="$(IFS='|'; echo "${PATTERNS[*]}")"

# Prints "line:content" for offending lines of stdin; returns 0 when something matched.
scan() {
  grep -inE -- "$regex"
}

usage() {
  echo "usage: $0 commits <base-ref> <head-ref> | text <file|->" >&2
  exit 2
}

mode="${1:-}"
case "$mode" in
  commits)
    [[ $# -eq 3 ]] || usage
    for ref in "$2" "$3"; do
      git rev-parse --verify --quiet "$ref^{commit}" >/dev/null || { echo "error: unknown ref '$ref'" >&2; exit 2; }
    done
    found=0
    while IFS= read -r sha; do
      if hits="$(git log -1 --format=%B "$sha" | scan)"; then
        found=1
        while IFS= read -r hit; do
          echo "AI attribution in commit ${sha:0:12} line $hit" >&2
        done <<<"$hits"
      fi
    done < <(git rev-list "$2..$3")
    ;;
  text)
    [[ $# -eq 2 ]] || usage
    found=0
    if [[ "$2" == "-" ]]; then input="$(cat)"; else input="$(cat -- "$2")"; fi
    if hits="$(printf '%s\n' "$input" | scan)"; then
      found=1
      while IFS= read -r hit; do
        echo "AI attribution at line $hit" >&2
      done <<<"$hits"
    fi
    ;;
  *) usage ;;
esac

if [[ "$found" -ne 0 ]]; then
  echo "error: remove AI attribution (Co-authored-by trailers, 'Generated with ...' lines)." >&2
  exit 1
fi
echo "no AI attribution found"
