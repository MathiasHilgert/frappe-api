#!/usr/bin/env bash
# Runs a command with the secrets of one Bitwarden Secrets Manager project injected as environment variables
# (bws run). Values stay in the child's environment: nothing is printed or written to disk. See docs/secrets.md.
set -euo pipefail

SCRIPTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly SCRIPTS_DIR
readonly BWS_CONFIG="$SCRIPTS_DIR/bws.toml"
readonly BWS_PROFILE_NAME="frappe-eu"
readonly SERVER="https://vault.bitwarden.eu"
readonly DEFAULT_PROJECT="frappe-dev"
# Secret names become environment variables of the command, so no secret may steer the shell, the dynamic linker,
# the JVM, Gradle, Spring or bws itself. Names are FRAPPE_* or a known third-party key name (docs/secrets.md).
readonly RESERVED_NAMES=" PATH HOME SHELL USER LOGNAME IFS ENV BASH_ENV CDPATH GLOBIGNORE PS4 PROMPT_COMMAND
 SHELLOPTS BASHOPTS TMPDIR CLASSPATH NODE_OPTIONS PERL5OPT RUBYOPT PYTHONPATH PYTHONSTARTUP "
readonly RESERVED_PREFIXES="LD_ DYLD_ BASH_FUNC_ BWS_ JAVA_ JDK_ _JAVA_ GRADLE_ SPRING_ GIT_"

usage() {
	cat <<EOF
Usage: scripts/with-secrets.sh [--project NAME] [--dry-run] [--] COMMAND [ARG...]

Runs COMMAND with the secrets of a Bitwarden Secrets Manager project ($SERVER) as environment variables.

Options:
  --project NAME  project to read (default: \${FRAPPE_SECRETS_PROJECT:-$DEFAULT_PROJECT})
  --dry-run       show what would run and which prerequisites are missing; contacts nothing
  -h, --help      show this help

Environment:
  BWS_ACCESS_TOKEN        access token of the project's read-only machine account (required)
  FRAPPE_SECRETS_PROJECT  default project when --project is not given

Example:
  scripts/with-secrets.sh ./gradlew bootRun
EOF
}

# The app itself never needs Bitwarden: the local profile has defaults for everything it reads.
local_path_hint() {
	cat >&2 <<EOF

Without Bitwarden the local profile still works: ./gradlew bootRun (defaults in application-local.properties).
Setup, tokens and the secrets inventory: docs/secrets.md
EOF
}

missing_bws() {
	cat >&2 <<EOF
with-secrets: the Bitwarden Secrets Manager CLI (bws) is not installed or not on PATH.
Install bws 2.1.0 or later from https://github.com/bitwarden/sdk-sm/releases (or: cargo install bws --locked).
EOF
	local_path_hint
}

missing_token() {
	cat >&2 <<EOF
with-secrets: BWS_ACCESS_TOKEN is not set.
Ask the secrets owner for an access token of the read-only machine account of project '$1'
(Secrets Manager > Machine accounts > $1-reader > Access tokens), keep it in your password manager and
export it in your shell session only: export BWS_ACCESS_TOKEN=... (never in a file inside the repository).
EOF
	local_path_hint
}

is_reserved() {
	local prefix
	[[ "$RESERVED_NAMES" == *[[:space:]]"$1"[[:space:]]* ]] && return 0
	for prefix in $RESERVED_PREFIXES; do
		[[ "$1" == "$prefix"* ]] && return 0
	done
	return 1
}

project="${FRAPPE_SECRETS_PROJECT:-$DEFAULT_PROJECT}"
dry_run=false
while (($# > 0)); do
	case "$1" in
	-h | --help)
		usage
		exit 0
		;;
	--project)
		[[ -n "${2:-}" ]] || { echo "with-secrets: --project needs a name" >&2; usage >&2; exit 2; }
		project="$2"
		shift 2
		;;
	--dry-run)
		dry_run=true
		shift
		;;
	--)
		shift
		break
		;;
	-*)
		echo "with-secrets: unknown option: $1" >&2
		usage >&2
		exit 2
		;;
	*) break ;;
	esac
done

if (($# == 0)); then
	echo "with-secrets: no command given" >&2
	usage >&2
	exit 2
fi

# bws run joins its arguments with spaces and runs the result with `<shell> -c`, so the shell parses them again.
# %q quotes each argument in bash syntax (spaces, quotes, $, newlines as $'...'), which only bash reads back
# unchanged: hence --shell bash below instead of the default sh.
printf -v command_line '%q ' "$@"
command_line="${command_line% }"

if [[ "$dry_run" == true ]]; then
	if command -v bws >/dev/null 2>&1; then
		echo "bws: $(bws --version 2>/dev/null || echo 'found, version unknown')"
	else
		echo "bws: not found"
	fi
	if [[ -n "${BWS_ACCESS_TOKEN:-}" ]]; then
		echo "BWS_ACCESS_TOKEN: set"
	else
		echo "BWS_ACCESS_TOKEN: not set"
	fi
	echo "server: $SERVER (profile $BWS_PROFILE_NAME in $BWS_CONFIG, no state file)"
	echo "project: $project"
	echo "would run: bws run --project-id <id of $project> --shell bash -- $command_line"
	exit 0
fi

command -v bws >/dev/null 2>&1 || { missing_bws; exit 1; }
[[ -n "${BWS_ACCESS_TOKEN:-}" ]] || { missing_token "$project"; exit 1; }

# The committed profile pins the EU server and opts out of the state file; a server URL would override it.
# UUIDs as variable names would bypass the name check below and the names the app reads.
export BWS_CONFIG_FILE="$BWS_CONFIG" BWS_PROFILE="$BWS_PROFILE_NAME"
unset BWS_SERVER_URL BWS_UUIDS_AS_KEYNAMES

projects="$(bws project list --output tsv --color no)" || {
	echo "with-secrets: could not list projects on $SERVER; is BWS_ACCESS_TOKEN valid and not expired?" >&2
	exit 1
}
project_id="$(awk -F '\t' -v name="$project" 'NR > 1 && $2 == name { print $1 }' <<<"$projects")"
if [[ -z "$project_id" || "$project_id" == *$'\n'* ]]; then
	cat >&2 <<EOF
with-secrets: project '$project' is not readable with this token (or its name is not unique).
Each token belongs to one read-only machine account per project; use the token of '$project' or pick another
project with --project. See docs/secrets.md.
EOF
	exit 1
fi

# Refuse reserved names before running anything. Only the names are kept: the listing (values included) stays in
# this variable, never on screen or disk, and is dropped at once.
listing="$(bws secret list "$project_id" --output tsv --color no)" || {
	echo "with-secrets: could not list the secrets of '$project'." >&2
	exit 1
}
# Rows start with a secret id (36 characters of hex and dashes). A line of a multi-line value that happens to look
# like one only adds a name to check, so the check can refuse too much but never miss a key.
names="$(awk -F '\t' 'NR > 1 && length($1) == 36 && $1 ~ /^[0-9a-f-]+$/ { print $2 }' <<<"$listing")"
unset listing
while IFS= read -r name; do
	if [[ -n "$name" ]] && is_reserved "$name"; then
		cat >&2 <<EOF
with-secrets: project '$project' has a secret named '$name', a reserved variable that would control the command
(shell, linker, JVM, Gradle, Spring or bws). Rename it to FRAPPE_* or a known third-party key name; nothing was run.
EOF
		exit 1
	fi
done <<<"$names"

exec bws run --project-id "$project_id" --shell bash -- "$command_line"
