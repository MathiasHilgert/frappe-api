#!/usr/bin/env bash
# Generates the repetitive skeleton of a use case so a writer only writes logic and tests: `just scaffold <kind> ...`.
# Whole files come from the `dotnet new` item templates in eng/templates (registered in a throwaway hive, never globally);
# what has to be appended to an existing file (errors, messages, integration events) is done here, and anything unsafe to guess is printed as a TODO.
# SCAFFOLD_ROOT overrides the repository root (the architecture test scaffolds into a copy).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
root="${SCAFFOLD_ROOT:-$repo}"
export LC_ALL=C

usage() {
    cat >&2 <<'USAGE'
just scaffold command  <Module> <Area> <UseCase>
just scaffold query    <Module> <Area> <UseCase>
just scaffold error    <Module> <code> <kind> "<en>" "<es>" "<pt-BR>" [ConstName]    kind: notfound | forbidden | conflict | invalid | rule
just scaffold event    <Module> <Area>/<Name> "<Field:Type,Field:Type>"             the event gets OccurredAt/At added
just scaffold endpoint <Module> <Area> <UseCase> <get|post|put|patch|delete> </route>
just scaffold bruno    <folder> "<name>" <method> </url> [status]                     folder under tools/bruno
USAGE
    exit 2
}

die() { echo "scaffold: $*" >&2; exit 1; }
pascal() { [[ "$1" =~ ^[A-Z][A-Za-z0-9]*$ ]] || die "'$1' is not PascalCase"; }

hive="$(mktemp -d)"
stage=""
trap 'rm -rf "$hive" "$stage"' EXIT
new() { # <template> <output dir> <args...>
    local template="$1" out="$2"
    shift 2
    mkdir -p "$out"
    [[ -n "${installed:-}" ]] || {
        dotnet new install "$repo/eng/templates" --force --debug:custom-hive "$hive" >/dev/null
        installed=1
    }
    dotnet new "$template" --debug:custom-hive "$hive" -o "$out" "$@" >/dev/null || die "template $template failed (does a file already exist in $out?)"
}

module_dir() { # <Module> -> src/Modules/<Module>, checked
    [[ -d "$root/src/Modules/$1" ]] || die "no module $1 under src/Modules"
    echo "src/Modules/$1"
}

append_member() { # <file> <text>: puts the text before the closing brace of the class, the last line of the file
    local file="$1" text="$2"
    [[ "$(tail -n 1 "$file")" == "}" ]] || die "$file does not end with a closing brace: add this by hand:"$'\n'"$text"
    head -n -1 "$file" >"$file.tmp"
    printf '\n%s\n}\n' "$text" >>"$file.tmp"
    mv "$file.tmp" "$file"
}

usecase() { # <command|query> <Module> <Area> <UseCase>
    local kind="$1" module="${2:-}" area="${3:-}" name="${4:-}"
    [[ -n "$name" ]] || usage
    pascal "$module"; pascal "$area"; pascal "$name"
    local md access
    md="$(module_dir "$module")"
    access="$(find "$root/$md/Frappe.$module.Application" -maxdepth 3 -name '*Access.cs' -not -path '*/obj/*' 2>/dev/null | head -n 1 | xargs -r basename -s .cs)"
    local isQuery=false
    [[ "$kind" == query ]] && isQuery=true
    new usecase "$root/$md/Frappe.$module.Application/$area/$name" -n "$name" --Module "$module" --Area "$area" --IsQuery "$isQuery" --AccessType "$access"
    if [[ -d "$root/tests/Modules/$module/Frappe.$module.UnitTests" ]]; then
        new usecase-test "$root/tests/Modules/$module/Frappe.$module.UnitTests/$area" -n "$name" --Module "$module" --Area "$area"
    else
        echo "TODO: Frappe.$module.UnitTests does not exist; write the test of $name there."
    fi
    echo "Next: write the failing test, give $name its fields and result type, write the handler (it throws NotImplementedException now)."
    [[ -z "$access" ]] || echo "Next: decide who may do it first through $access (see the FILL IN in the handler)."
}

error() { # <Module> <code> <kind> <en> <es> <pt-BR> [ConstName]
    local module="${1:-}" code="${2:-}" kind="${3:-}" en="${4:-}" es="${5:-}" pt="${6:-}" const="${7:-}"
    [[ -n "$pt" ]] || usage
    pascal "$module"
    [[ "$code" =~ ^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){2,}$ ]] || die "code '$code' is not like orders.order.missing"
    local type
    case "$kind" in
        notfound) type=NotFoundError ;;
        forbidden) type=ForbiddenError ;;
        conflict) type=ConflictError ;;
        invalid) type=InvalidValueError ;;
        rule) type=RuleViolationError ;;
        *) die "kind '$kind' is not one of notfound, forbidden, conflict, invalid, rule" ;;
    esac
    if [[ -z "$const" ]]; then
        IFS=. read -r -a parts <<<"$code"
        parts=("${parts[@]:1}")
        [[ "${parts[0]}" != "${code%%.*}" ]] || parts=("${parts[@]:1}")
        for part in "${parts[@]}"; do
            IFS=_ read -r -a words <<<"$part"
            for word in "${words[@]}"; do const+="${word^}"; done
        done
    fi
    local md codes errors messages language
    md="$(module_dir "$module")"
    codes="$root/$md/Frappe.$module.Domain/${module}ErrorCodes.cs"
    errors="$root/$md/Frappe.$module.Application/${module}Errors.cs"
    messages="$root/$md/Frappe.$module.Http/Resources/Messages"
    for file in "$codes" "$errors" "$messages/en.json" "$messages/es.json" "$messages/pt-BR.json"; do
        [[ -f "$file" ]] || die "$file does not exist"
    done
    ! grep -qF "\"$code\"" "$codes" || die "$code already exists"
    ! grep -qF "public const string $const " "$codes" || die "constant $const already exists in ${module}ErrorCodes"
    command -v jq >/dev/null || die "jq is required to append the messages"
    for language in en es pt-BR; do
        jq -e . "$messages/$language.json" >/dev/null || die "$messages/$language.json is not valid JSON"
        ! jq -e --arg k "$code" 'has($k)' "$messages/$language.json" >/dev/null || die "$code is already in $language.json"
    done
    # All-or-nothing: every new content is built in a staging directory and moved over the originals only once all of them exist.
    stage="$(mktemp -d)"
    cp "$codes" "$stage/codes.cs"
    cp "$errors" "$stage/errors.cs"
    append_member "$stage/codes.cs" "    /// <summary>FILL IN: say when $code is answered and which parameters its message names.</summary>
    public const string $const = \"$code\";"
    append_member "$stage/errors.cs" "    /// <summary>FILL IN: say when $code is answered.</summary>
    public static $type $const { get; } = new(${module}ErrorCodes.$const);"
    local text
    for language in en es pt-BR; do
        case "$language" in en) text="$en" ;; es) text="$es" ;; *) text="$pt" ;; esac
        jq --indent 2 --arg key "$code" --arg text "$text" '. + {($key): $text}' "$messages/$language.json" >"$stage/$language.json" || { rm -rf "$stage"; die "could not append the message to $language.json; nothing was changed"; }
    done
    mv "$stage/codes.cs" "$codes"
    mv "$stage/errors.cs" "$errors"
    for language in en es pt-BR; do mv "$stage/$language.json" "$messages/$language.json"; done
    rm -rf "$stage"
    echo "Added $code as ${module}ErrorCodes.$const and ${module}Errors.$const (fill the two TODO summaries); messages appended to en, es and pt-BR."
    echo "Next: a parametrised error takes its parameters as a method, not a property: change it by hand."
}

event() { # <Module> <Area>/<Name> <Field:Type,...>
    local module="${1:-}" target="${2:-}" fields="${3:-}"
    [[ "$target" == */* && -n "$fields" ]] || usage
    local area="${target%%/*}" name="${target##*/}"
    pascal "$module"; pascal "$area"; pascal "$name"
    local md snake type contract domain="" arg
    md="$(module_dir "$module")"
    snake="$(sed -E 's/([a-z0-9])([A-Z])/\1_\2/g' <<<"$name" | tr '[:upper:]' '[:lower:]')"
    type="frappe.${module,,}.$snake.v1"
    local -a args=() reads=()
    IFS=, read -r -a pairs <<<"$fields"
    for arg in "${pairs[@]}"; do
        [[ "$arg" =~ ^([A-Z][A-Za-z0-9]*):([A-Za-z0-9<>?,\.]+)$ ]] || die "field '$arg' is not Name:Type"
        args+=("${BASH_REMATCH[2]} ${BASH_REMATCH[1]}")
        reads+=("e.${BASH_REMATCH[1]}")
    done
    local list
    list="$(printf '%s, ' "${args[@]}")"
    new event-contract "$root/$md/Frappe.$module.Contracts/$area" -n "$name" --Module "$module" --Area "$area" --EventType "$type" --ContractFields "${list}DateTimeOffset OccurredAt"
    new event-domain "$root/$md/Frappe.$module.Domain/$area" -n "$name" --Module "$module" --Area "$area" --DomainFields "${list}Instant At"
    local translator="$root/$md/Frappe.$module.Infrastructure/Messaging/${module}IntegrationEvents.cs"
    local alias="Published$name" arm
    arm="        $name e => [new $alias($(printf '%s, ' "${reads[@]}")e.At.ToDateTimeOffset())],"
    local using="using $alias = Frappe.$module.Contracts.$area.$name;"
    if [[ -f "$translator" ]] && grep -q '^using Published' "$translator" && grep -q '^        _ => \[\],' "$translator"; then
        awk -v new="$using" -v arm="$arm" '
            /^using Published/ { seen = 1; if (!done && $0 > new) { print new; done = 1 } print; next }
            { if (seen && !done) { print new; done = 1 } }
            /^        _ => \[\],/ { print arm }
            { print }' "$translator" >"$translator.tmp"
        mv "$translator.tmp" "$translator"
        grep -q "^using Frappe.$module.Domain.$area;" "$translator" || echo "TODO: add 'using Frappe.$module.Domain.$area;' to ${module}IntegrationEvents.cs."
    else
        echo "TODO: add to ${module}IntegrationEvents.cs the line  $using  and, before '_ => [],', the arm:"
        echo "$arm"
    fi
    echo "Next: the domain event carries domain types (OrderId, Reference...): change them, and the arm, to read .Value; then 'just contracts update'."
    echo "Event type: $type"
}

endpoint() { # <Module> <Area> <UseCase> <verb> <route>
    local module="${1:-}" area="${2:-}" name="${3:-}" verb="${4:-}" route="${5:-}"
    [[ -n "$route" ]] || usage
    pascal "$module"; pascal "$area"; pascal "$name"
    [[ "$route" == /* ]] || die "route '$route' must start with /"
    case "$verb" in get | post | put | patch | delete) ;; *) die "verb '$verb' is not get, post, put, patch or delete" ;; esac
    local md
    md="$(module_dir "$module")"
    local first=true # the generated usings are ordered: Frappe.<Module> before Frappe.Http only when the module sorts first
    [[ "$module" < "Http" ]] || first=false
    local isQuery=false record="$root/$md/Frappe.$module.Application/$area/$name/$name.cs" # a query's endpoint answers 200 through IQueryHandler; a command's, 204 through ICommandHandler
    [[ -f "$record" ]] || die "no use case $module/$area/$name: run 'just scaffold command|query $module $area $name' first"
    ! grep -q ': IQuery<' "$record" || isQuery=true
    new endpoint "$root/$md/Frappe.$module.Http/$area" -n "$name" --Module "$module" --Area "$area" --IsQuery "$isQuery" --ModuleFirst "$first" --Verb "Map${verb^}" --Route "$route"
    echo "Next: map it in ${module}Http.cs with  <group>.Map${name}Endpoints();  (the group variable is the module's), then give it its request, ETag and error answers, and 'just contracts update'."
}

bruno() { # <folder> <name> <method> <url> [status]
    local folder="${1:-}" name="${2:-}" method="${3:-}" url="${4:-}" status="${5:-200}"
    [[ -n "$url" ]] || usage
    local dir="$root/tools/bruno/$folder"
    [[ -d "$dir" ]] || die "no folder tools/bruno/$folder"
    method="${method^^}"
    local seq file
    seq="$(( $(grep -h '^  seq:' "$dir"/*.yml 2>/dev/null | sed -E 's/[^0-9]//g' | sort -n | tail -n 1 | sed 's/^$/0/') + 1 ))"
    file="$dir/$(tr '[:upper:] ' '[:lower:]-' <<<"$name" | sed -E 's/[^a-z0-9-]//g').yml"
    [[ ! -e "$file" ]] || die "$file already exists"
    [[ "$url" != /* ]] || url="{{baseUrl}}$url"
    {
        printf 'info:\n  name: %s\n  type: http\n  seq: %s\n\nhttp:\n  method: %s\n  url: "%s"\n' "$name" "$seq" "$method" "$url"
        if [[ "$method" == POST || "$method" == PUT || "$method" == PATCH ]]; then
            printf '  headers:\n    - name: Content-Type\n      value: "application/json"\n  body:\n    type: json\n    data: |-\n      {}\n'
        fi
        printf '  auth: inherit\n\nruntime:\n  assertions:\n    - expression: res.status\n      operator: eq\n      value: "%s"\n\n' "$status"
        printf 'settings:\n  encodeUrl: true\n\ndocs: |-\n  `%s %s`: FILL IN: say what this request proves.\n' "$method" "$url"
    } >"$file"
    echo "Created $file (seq $seq)."
}

kind="${1:-}"
[[ $# -gt 0 ]] && shift
case "$kind" in
    command | query) usecase "$kind" "$@" ;;
    error) error "$@" ;;
    event) event "$@" ;;
    endpoint) endpoint "$@" ;;
    bruno) bruno "$@" ;;
    *) usage ;;
esac
