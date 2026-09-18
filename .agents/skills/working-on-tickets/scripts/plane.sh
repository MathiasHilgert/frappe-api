#!/usr/bin/env bash
# Deterministic Plane helper for the Frappé API project.
# Requires: PLANE_API_KEY in the environment, curl, jq.
set -euo pipefail

readonly BASE="https://api.plane.so/api/v1/workspaces/nulled-software/projects/1eccc155-7124-4a0d-a0eb-0d5ced1ca560"
readonly WEB="https://app.plane.so/nulled-software/browse"

usage() {
  cat <<'EOF'
Usage: plane.sh <command> [args]

  show FAPI-N                                  Title, state, labels, module and description as text
  list [state]                                 Work items, optionally filtered by state
                                               (backlog|todo|in-progress|in-review|done|cancelled)
  move FAPI-N <state>                          Change state (todo|in-progress|in-review|done|cancelled)
  comment FAPI-N "text"                        Add a comment (HTML-escaped, minified)
  create --title T --module M --labels a,b --html-file F
                                               Create a Todo work item, assign it to module M,
                                               labels by name (e.g. module:identity,type:feature,size:S)
EOF
}

die() { printf 'plane.sh: %s\n' "$*" >&2; exit 1; }

require() {
  command -v curl >/dev/null || die "curl is required"
  command -v jq >/dev/null || die "jq is required"
  [[ -n "${PLANE_API_KEY:-}" ]] || die "PLANE_API_KEY is not set"
}

# api METHOD PATH [JSON_BODY]
api() {
  local method=$1 path=$2 body=${3:-} out status
  local args=(-sS -X "$method" -H "X-API-Key: ${PLANE_API_KEY}" -H "Content-Type: application/json" -w '\n%{http_code}')
  [[ -n "$body" ]] && args+=(--data "$body")
  out=$(curl "${args[@]}" "${BASE}${path}") || die "request failed: $method $path"
  status=${out##*$'\n'}
  out=${out%$'\n'*}
  [[ "$status" =~ ^2 ]] || die "HTTP $status on $method $path: $(printf '%s' "$out" | head -c 300)"
  printf '%s' "$out"
}

state_id() {
  case "$1" in
    backlog) echo 73ebf055-9286-44b3-bd29-12758baacd02 ;;
    todo) echo 44723e9d-b66b-4b90-9ef8-06308cc79643 ;;
    in-progress) echo 75e2726a-e25a-4254-8b6c-6c86843cca3e ;;
    in-review) echo 9a4c246a-374d-4822-95c0-c6b41fb420f4 ;;
    done) echo 75dbd52d-7a57-41dd-a662-e86da8d1a955 ;;
    cancelled) echo d1cab20e-ee2a-4454-b839-ac8e3c0dbd88 ;;
    *) die "unknown state '$1' (backlog|todo|in-progress|in-review|done|cancelled)" ;;
  esac
}

# All work items (follows cursor pagination), expanded with state and labels.
all_items() {
  local cursor="" page items="[]"
  while :; do
    page=$(api GET "/work-items/?per_page=100&expand=state,labels${cursor:+&cursor=$cursor}")
    items=$(jq -s '.[0] + .[1].results' <(printf '%s' "$items") <(printf '%s' "$page"))
    [[ $(jq -r '.next_page_results' <<<"$page") == "true" ]] || break
    cursor=$(jq -r '.next_cursor' <<<"$page")
  done
  printf '%s' "$items"
}

resolve_id() {
  local key=$1 seq id
  [[ "$key" =~ ^FAPI-([0-9]+)$ ]] || die "expected FAPI-N, got '$key'"
  seq=${BASH_REMATCH[1]}
  id=$(all_items | jq -r --argjson s "$seq" '.[] | select(.sequence_id == $s) | .id')
  [[ -n "$id" ]] || die "$key not found"
  printf '%s' "$id"
}

minify() { tr '\n\t' '  ' | sed -E 's/>[[:space:]]+</></g; s/^[[:space:]]+//; s/[[:space:]]+$//'; }

html_to_text() {
  sed -E 's#</(p|h[1-6]|li|tr|pre)>#\n#g; s#</t[dh]>#\t#g; s#<br ?/?>#\n#g; s#<[^>]+>##g' |
    sed -E 's/&lt;/</g; s/&gt;/>/g; s/&quot;/"/g; s/&#39;|&apos;/'"'"'/g; s/&nbsp;/ /g; s/&amp;/\&/g'
}

cmd_show() {
  [[ $# -eq 1 ]] || die "usage: show FAPI-N"
  local id item
  id=$(resolve_id "$1")
  item=$(api GET "/work-items/${id}/?expand=state,labels")
  jq -r --arg key "$1" --arg web "$WEB" '
    "\($key): \(.name)",
    "URL:    \($web)/\($key)/",
    "State:  \(.state.name)",
    "Labels: \([.labels[].name] | sort | join(", "))",
    "Module: \(.min_module_name // "-")",
    "",
    "Description:"' <<<"$item"
  jq -r '.description_html // ""' <<<"$item" | html_to_text
}

cmd_list() {
  local filter=""
  [[ $# -le 1 ]] || die "usage: list [state]"
  [[ $# -eq 1 ]] && filter=$(state_id "$1")
  all_items | jq -r --arg f "$filter" '
    sort_by(.sequence_id)[]
    | select($f == "" or .state.id == $f)
    | "FAPI-\(.sequence_id)\t\(.state.name)\t\(.min_module_name // "-")\t\(.name)"'
}

cmd_move() {
  [[ $# -eq 2 ]] || die "usage: move FAPI-N todo|in-progress|in-review|done|cancelled"
  local id sid
  sid=$(state_id "$2")
  id=$(resolve_id "$1")
  api PATCH "/work-items/${id}/" "$(jq -nc --arg s "$sid" '{state: $s}')" |
    jq -r --arg key "$1" --arg to "$2" '"\($key) -> \($to) (\(.state))"'
}

cmd_comment() {
  [[ $# -eq 2 && -n "$2" ]] || die "usage: comment FAPI-N \"text\""
  local id html
  id=$(resolve_id "$1")
  html=$(printf '%s' "$2" | sed -E 's/&/\&amp;/g; s/</\&lt;/g; s/>/\&gt;/g' |
    awk 'BEGIN{ORS=""} {print "<p>" $0 "</p>"}')
  api POST "/work-items/${id}/comments/" "$(jq -nc --arg h "$html" '{comment_html: $h}')" |
    jq -r --arg key "$1" '"comment \(.id) added to \($key)"'
}

cmd_create() {
  local title="" module="" labels="" html_file=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --title) title=${2:-}; shift 2 ;;
      --module) module=${2:-}; shift 2 ;;
      --labels) labels=${2:-}; shift 2 ;;
      --html-file) html_file=${2:-}; shift 2 ;;
      *) die "unknown option '$1'" ;;
    esac
  done
  [[ -n "$title" && -n "$module" && -n "$labels" && -n "$html_file" ]] ||
    die "usage: create --title T --module M --labels a,b --html-file F"
  [[ -r "$html_file" ]] || die "cannot read $html_file"

  local module_id label_ids html item id seq
  module_id=$(api GET "/modules/?per_page=100" | jq -r --arg m "$module" '.results[] | select(.name == $m) | .id')
  [[ -n "$module_id" ]] || die "module '$module' not found"
  label_ids=$(api GET "/labels/?per_page=100" | jq -c --arg l "$labels" '
    ($l | split(",") | map(gsub("^ +| +$"; ""))) as $want
    | [.results[] | select(.name as $n | $want | index($n))] as $found
    | if ($found | length) == ($want | length) then [$found[].id]
      else error("unknown label(s): \($want - [$found[].name] | join(", "))") end') ||
    die "label lookup failed"
  html=$(minify <"$html_file")

  item=$(api POST "/work-items/" "$(jq -nc --arg t "$title" --arg h "$html" --argjson l "$label_ids" \
    --arg s "$(state_id todo)" '{name: $t, description_html: $h, labels: $l, state: $s}')")
  id=$(jq -r '.id' <<<"$item")
  seq=$(jq -r '.sequence_id' <<<"$item")
  api POST "/modules/${module_id}/module-issues/" "$(jq -nc --arg i "$id" '{issues: [$i]}')" >/dev/null
  printf 'FAPI-%s created: %s/FAPI-%s/\n' "$seq" "$WEB" "$seq"
}

main() {
  [[ $# -ge 1 ]] || { usage; exit 1; }
  local cmd=$1; shift
  case "$cmd" in
    -h|--help|help) usage ;;
    show|list|move|comment|create) require; "cmd_$cmd" "$@" ;;
    *) usage >&2; die "unknown command '$cmd'" ;;
  esac
}

main "$@"
