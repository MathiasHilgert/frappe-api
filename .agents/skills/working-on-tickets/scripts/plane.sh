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
  create --title T --module M --labels a,b --html-file F [--estimate V]
                                               Create a Todo work item, assign it to module M,
                                               labels by name (e.g. module:identity,type:feature,size:S),
                                               optionally set the estimate point to value V
  relate FAPI-A blocked-by|blocks|relates-to FAPI-B
                                               Create a work-item relation between two tickets
  edit FAPI-N [--title T] [--html-file F] [--estimate V]
                                               Update title, description and/or estimate point
  modules                                      List modules (name, id)
  labels                                       List labels (name, id)
  label-create NAME [--color HEX]              Create a label
  module-create NAME                           Create a module
  pages                                        List Plane pages (id, name)
  page-get <name|id> [--out F]                 Write the page's HTML to F (default <name>.html)
  page-put <name|id> --html-file F [--dry-run] Update a page's HTML (minified; rejects whitespace
                                               between tags and tables whose colwidths don't sum
                                               to ~1000 per row). --dry-run validates and prints
                                               the request without sending it.
EOF
}

die() { printf 'plane.sh: %s\n' "$*" >&2; exit 1; }

readonly USER_AGENT="frappe-api-plane-sh/1.0"

require() {
  command -v curl >/dev/null || die "curl is required"
  command -v jq >/dev/null || die "jq is required"
  [[ -n "${PLANE_API_KEY:-}" ]] || die "PLANE_API_KEY is not set"
}

# api METHOD PATH [JSON_BODY]
api() {
  local method=$1 path=$2 body=${3:-} out status
  local args=(-sS -X "$method" -H "X-API-Key: ${PLANE_API_KEY}" -H "User-Agent: ${USER_AGENT}" -H "Content-Type: application/json" -w '\n%{http_code}')
  [[ -n "$body" ]] && args+=(--data "$body")
  out=$(curl "${args[@]}" "${BASE}${path}") || die "request failed: $method $path"
  status=${out##*$'\n'}
  out=${out%$'\n'*}
  [[ "$status" =~ ^2 ]] || die "HTTP $status on $method $path: $(printf '%s' "$out" | head -c 300)"
  printf '%s' "$out"
}

# send METHOD PATH BODY — like api(), but when PLANE_DRY_RUN=1 prints the
# request instead of sending it and ends the command successfully, so the
# confirmation a caller chains after it is never printed for an unsent request.
send() {
  local method=$1 path=$2 body=$3
  if [[ "${PLANE_DRY_RUN:-0}" == "1" ]]; then
    printf 'DRY RUN: %s %s\n' "$method" "$path"
    jq '.' <<<"$body"
    exit 0
  fi
  api "$method" "$path" "$body" >/dev/null
}

# Resolves a plain estimate value (e.g. "3" or "M") to the id of the
# project's active estimate point. Fails clearly if estimates are disabled.
resolve_estimate_point() {
  local value=$1 project estimate_id points point_id
  project=$(api GET "/")
  estimate_id=$(jq -r '.estimate // empty' <<<"$project")
  [[ -n "$estimate_id" ]] || die "project has no estimates enabled"
  points=$(api GET "/estimates/${estimate_id}/estimate-points/")
  point_id=$(jq -r --arg v "$value" '
    (if type == "object" then .results else . end)
    | .[] | select(.value == $v) | .id' <<<"$points")
  [[ -n "$point_id" ]] || die "estimate value '$value' not found for this project's estimate"
  printf '%s' "$point_id"
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
  local title="" module="" labels="" html_file="" estimate=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --title) title=${2:-}; shift 2 ;;
      --module) module=${2:-}; shift 2 ;;
      --labels) labels=${2:-}; shift 2 ;;
      --html-file) html_file=${2:-}; shift 2 ;;
      --estimate) estimate=${2:-}; shift 2 ;;
      *) die "unknown option '$1'" ;;
    esac
  done
  [[ -n "$title" && -n "$module" && -n "$labels" && -n "$html_file" ]] ||
    die "usage: create --title T --module M --labels a,b --html-file F [--estimate V]"
  [[ -r "$html_file" ]] || die "cannot read $html_file"

  local module_id label_ids html item id seq estimate_id=""
  module_id=$(api GET "/modules/?per_page=100" | jq -r --arg m "$module" '.results[] | select(.name == $m) | .id')
  [[ -n "$module_id" ]] || die "module '$module' not found"
  label_ids=$(api GET "/labels/?per_page=100" | jq -c --arg l "$labels" '
    ($l | split(",") | map(gsub("^ +| +$"; ""))) as $want
    | [.results[] | select(.name as $n | $want | index($n))] as $found
    | if ($found | length) == ($want | length) then [$found[].id]
      else error("unknown label(s): \($want - [$found[].name] | join(", "))") end') ||
    die "label lookup failed"
  html=$(minify <"$html_file")
  [[ -n "$estimate" ]] && estimate_id=$(resolve_estimate_point "$estimate")

  local filter='{name: $t, description_html: $h, labels: $l, state: $s}'
  local jq_args=(--arg t "$title" --arg h "$html" --argjson l "$label_ids" --arg s "$(state_id todo)")
  if [[ -n "$estimate_id" ]]; then
    jq_args+=(--arg e "$estimate_id")
    filter+=' + {estimate_point: $e}'
  fi
  item=$(api POST "/work-items/" "$(jq -nc "${jq_args[@]}" "$filter")")
  id=$(jq -r '.id' <<<"$item")
  seq=$(jq -r '.sequence_id' <<<"$item")
  api POST "/modules/${module_id}/module-issues/" "$(jq -nc --arg i "$id" '{issues: [$i]}')" >/dev/null
  printf 'FAPI-%s created: %s/FAPI-%s/\n' "$seq" "$WEB" "$seq"
}

cmd_relate() {
  [[ $# -eq 3 ]] || die "usage: relate FAPI-A blocked-by|blocks|relates-to FAPI-B"
  local key_a=$1 rel=$2 key_b=$3 type id_a id_b body
  case "$rel" in
    blocked-by) type=blocked_by ;;
    blocks) type=blocking ;;
    relates-to) type=relates_to ;;
    *) die "unknown relation '$rel' (blocked-by|blocks|relates-to)" ;;
  esac
  id_a=$(resolve_id "$key_a")
  id_b=$(resolve_id "$key_b")
  body=$(jq -nc --arg t "$type" --arg i "$id_b" '{relation_type: $t, issues: [$i]}')
  send POST "/work-items/${id_a}/relations/" "$body" &&
    printf '%s %s %s\n' "$key_a" "$rel" "$key_b"
}

cmd_edit() {
  [[ $# -ge 1 ]] || die "usage: edit FAPI-N [--title T] [--html-file F] [--estimate V]"
  local key=$1; shift
  local title="" html_file="" estimate=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --title) title=${2:-}; shift 2 ;;
      --html-file) html_file=${2:-}; shift 2 ;;
      --estimate) estimate=${2:-}; shift 2 ;;
      *) die "unknown option '$1'" ;;
    esac
  done
  [[ -n "$title" || -n "$html_file" || -n "$estimate" ]] ||
    die "usage: edit FAPI-N [--title T] [--html-file F] [--estimate V]"

  local id estimate_id="" jq_args=() filter='{}'
  id=$(resolve_id "$key")
  if [[ -n "$title" ]]; then
    jq_args+=(--arg name "$title")
    filter+=' + {name: $name}'
  fi
  if [[ -n "$html_file" ]]; then
    [[ -r "$html_file" ]] || die "cannot read $html_file"
    jq_args+=(--arg description_html "$(minify <"$html_file")")
    filter+=' + {description_html: $description_html}'
  fi
  if [[ -n "$estimate" ]]; then
    estimate_id=$(resolve_estimate_point "$estimate")
    jq_args+=(--arg estimate_point "$estimate_id")
    filter+=' + {estimate_point: $estimate_point}'
  fi

  send PATCH "/work-items/${id}/" "$(jq -nc "${jq_args[@]}" "$filter")" &&
    printf '%s updated\n' "$key"
}

cmd_modules() {
  api GET "/modules/?per_page=100" | jq -r '.results[] | "\(.name)\t\(.id)"'
}

cmd_labels() {
  api GET "/labels/?per_page=100" | jq -r '.results[] | "\(.name)\t\(.id)"'
}

cmd_label-create() {
  [[ $# -ge 1 && -n "$1" ]] || die "usage: label-create NAME [--color HEX]"
  local name=$1; shift
  local color="" body
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --color) color=${2:-}; shift 2 ;;
      *) die "unknown option '$1'" ;;
    esac
  done
  if [[ -n "$color" ]]; then
    body=$(jq -nc --arg n "$name" --arg c "$color" '{name: $n, color: $c}')
  else
    body=$(jq -nc --arg n "$name" '{name: $n}')
  fi
  send POST "/labels/" "$body" &&
    printf 'label "%s" created\n' "$name"
}

cmd_module-create() {
  [[ $# -eq 1 && -n "$1" ]] || die "usage: module-create NAME"
  send POST "/modules/" "$(jq -nc --arg n "$1" '{name: $n}')" &&
    printf 'module "%s" created\n' "$1"
}

resolve_page_id() {
  local key=$1 ids n
  [[ "$key" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] &&
    { printf '%s' "$key"; return; }
  ids=$(api GET "/pages/?per_page=100" | jq -r --arg n "$key" '.results[] | select(.name == $n) | .id')
  n=$(grep -c . <<<"$ids" 2>/dev/null || true)
  [[ -n "$ids" ]] || die "page '$key' not found"
  [[ "$n" -eq 1 ]] || die "page '$key' is ambiguous ($n matches); use the page id"
  printf '%s' "$ids"
}

cmd_pages() {
  api GET "/pages/?per_page=100" | jq -r '.results[] | "\(.id)\t\(.name)"'
}

cmd_page-get() {
  [[ $# -ge 1 ]] || die "usage: page-get <name|id> [--out F]"
  local key=$1 out="" id html
  shift
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --out) out=${2:-}; shift 2 ;;
      *) die "unknown option '$1'" ;;
    esac
  done
  [[ -n "$out" ]] || out="${key}.html"
  id=$(resolve_page_id "$key")
  html=$(api GET "/pages/${id}/" | jq -r '.description_html // ""')
  printf '%s' "$html" >"$out"
  printf 'page %s written to %s\n' "$key" "$out"
}

# Minifies HTML (collapses whitespace between tags, preserving multiple or
# repeated <pre>...</pre> blocks verbatim for mermaid diagrams), validates
# that every table row's th/td colwidths (including comma-separated colspan
# values) sum to ~1000, and prints the minified HTML on success. Prints one
# error per offending row to stderr and exits 1 otherwise.
minify_and_validate_html() {
  local file=$1
  command -v perl >/dev/null || die "perl is required for page-put"
  [[ -r "$file" ]] || die "cannot read $file"
  perl -0777 -ne '
    my $html = $_;
    my @blocks;
    $html =~ s{(<pre\b.*?</pre>)}{
      push @blocks, $1;
      "\x00" . $#blocks . "\x00"
    }gse;
    $html =~ s/>\s+</></g;
    $html =~ s/^\s+//;
    $html =~ s/\s+$//;
    $html =~ s/\x00(\d+)\x00/$blocks[$1]/ge;

    my @errors;
    while ($html =~ m{<tr\b[^>]*>(.*?)</tr>}gs) {
      my $row = $1;
      my @widths;
      while ($row =~ /colwidth="?([\d,]+)"?/g) {
        push @widths, split(/,/, $1);
      }
      next unless @widths;
      my $sum = 0;
      $sum += $_ for @widths;
      push @errors, "table row colwidths [@widths] sum to $sum, expected ~1000"
        if abs($sum - 1000) > 50;
    }
    if (@errors) {
      print STDERR "$_\n" for @errors;
      exit 1;
    }
    print $html;
  ' "$file"
}

cmd_page-put() {
  [[ $# -ge 1 ]] || die "usage: page-put <name|id> --html-file F [--dry-run]"
  local key=$1 html_file="" dry_run=0
  shift
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --html-file) html_file=${2:-}; shift 2 ;;
      --dry-run) dry_run=1; shift ;;
      *) die "unknown option '$1'" ;;
    esac
  done
  [[ -n "$html_file" ]] || die "usage: page-put <name|id> --html-file F [--dry-run]"

  local id name html
  id=$(resolve_page_id "$key")
  html=$(minify_and_validate_html "$html_file") || die "html validation failed for $html_file"
  name=$(api GET "/pages/${id}/" | jq -r '.name')

  if [[ "$dry_run" -eq 1 ]]; then
    printf 'DRY RUN: would PUT /pages/%s/\n' "$id"
    printf 'name: %s\n' "$name"
    printf 'description_html (%d bytes):\n%s\n' "${#html}" "$html"
    return
  fi

  api PUT "/pages/${id}/" "$(jq -nc --arg n "$name" --arg h "$html" '{name: $n, description_html: $h}')" |
    jq -r --arg key "$key" '"page \($key) updated"'
}

main() {
  [[ $# -ge 1 ]] || { usage; exit 1; }
  local cmd=$1; shift
  case "$cmd" in
    -h|--help|help) usage ;;
    show|list|move|comment|create|relate|edit|modules|labels|label-create|module-create|pages|page-get|page-put)
      require; "cmd_$cmd" "$@" ;;
    *) usage >&2; die "unknown command '$cmd'" ;;
  esac
}

main "$@"
