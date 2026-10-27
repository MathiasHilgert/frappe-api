#!/usr/bin/env bash
# Fails when a commit message, the PR title or the PR body carries AI attribution.
#
# Usage:
#   eng/scripts/check-ai-attribution.sh <revision-range>   e.g. origin/main..HEAD
#   eng/scripts/check-ai-attribution.sh -                   check one message from stdin
#
# PR_TITLE and PR_BODY, when set, are checked too. HTML comments in the body are
# ignored because the pull request template explains this very rule inside them.
set -euo pipefail

# Case-insensitive extended regular expressions, one per attribution form.
patterns=(
  '^co-authored-by:.*(claude|anthropic|openai|chatgpt|gpt-[0-9]|copilot|gemini|cursor|codeium|windsurf|devin|aider|codex)'
  'noreply@(anthropic|openai)\.com'
  'generated (with|by) \[?(claude|chatgpt|copilot|gemini|cursor|codex|aider|devin|ai\b)'
  '^claude-session:'
  'claude\.(ai|com)/code'
  '🤖'
)

any_pattern=$(IFS='|'; echo "${patterns[*]}")
failures=0

# check <label> <text>: reports every line of <text> that matches a pattern.
check() {
  local label=$1 text=$2 match
  if match=$(grep -iE -- "$any_pattern" <<< "$text"); then
    echo "error: AI attribution in $label:" >&2
    echo "    ${match//$'\n'/$'\n'    }" >&2
    failures=$((failures + 1))
  fi
}

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <revision-range> | -" >&2
  exit 2
fi

if [[ $1 == "-" ]]; then
  check "the message from stdin" "$(cat)"
else
  shas=$(git rev-list "$1") # a separate assignment, so a bad range stops the script
  for sha in $shas; do
    check "commit ${sha:0:12}" "$(git log -1 --format=%B "$sha")"
  done
fi

if [[ -n ${PR_TITLE:-} ]]; then
  check "the PR title" "$PR_TITLE"
fi
if [[ -n ${PR_BODY:-} ]]; then
  check "the PR body" "$(perl -0pe 's/<!--.*?-->//gs' <<< "$PR_BODY")"
fi

if ((failures > 0)); then
  echo "Remove the AI attribution: the human author is the only author." >&2
  exit 1
fi
echo "No AI attribution found."
