#!/usr/bin/env bash
# Hermetic tests for scripts/with-secrets.sh: a fake `bws` on PATH stands in for Bitwarden, so no account,
# token or network is needed. Run: scripts/with-secrets.test.sh (also part of ./gradlew check).
set -euo pipefail

SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/with-secrets.sh"
readonly SCRIPT
readonly TOKEN_SENTINEL='0.token-sentinel-must-never-appear'
readonly SECRET_SENTINEL='secret-sentinel-must-never-appear'
readonly DEV_ID='11111111-1111-1111-1111-111111111111'
readonly PRODUCTION_ID='22222222-2222-2222-2222-222222222222'
readonly SECRET_ID='33333333-3333-3333-3333-333333333333'

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
FAILURES=0
TESTS=0

# A fake bws: logs its arguments and relevant environment (never values of secrets), lists projects from
# FAKE_PROJECTS and emulates `bws run` (injects FAKE_SECRET, drops the token, runs `<shell> -c "<args>"`).
mkdir -p "$WORK/bin" "$WORK/nobws" "$WORK/home"
cat >"$WORK/bin/bws" <<'FAKE'
#!/usr/bin/env bash
set -euo pipefail
{
	echo "args: $*"
	echo "BWS_CONFIG_FILE=${BWS_CONFIG_FILE:-<unset>}"
	echo "BWS_PROFILE=${BWS_PROFILE:-<unset>}"
	echo "BWS_SERVER_URL=${BWS_SERVER_URL:-<unset>}"
	echo "BWS_UUIDS_AS_KEYNAMES=${BWS_UUIDS_AS_KEYNAMES:-<unset>}"
} >>"$FAKE_LOG"
if [[ "${1:-}" == "--version" ]]; then
	echo "bws 2.1.0"
	exit 0
fi
[[ -n "${BWS_ACCESS_TOKEN:-}" ]] || { echo "Error: Missing access token" >&2; exit 1; }
if [[ "${1:-}" == "project" && "${2:-}" == "list" ]]; then
	printf 'ID\tName\tCreation Date\n'
	printf '%b' "${FAKE_PROJECTS:-}"
	exit 0
fi
if [[ "${1:-}" == "secret" && "${2:-}" == "list" ]]; then
	printf 'ID\tKey\tValue\tCreation Date\n'
	printf '%b' "${FAKE_SECRETS:-}"
	exit 0
fi
if [[ "${1:-}" == "run" ]]; then
	shift
	shell=sh
	while [[ "$1" != "--" ]]; do
		case "$1" in
		--shell) shell="$2"; shift 2 ;;
		--project-id) shift 2 ;;
		*) shift ;;
		esac
	done
	shift
	unset BWS_ACCESS_TOKEN
	export FAKE_SECRET="$FAKE_SECRET_VALUE"
	exec "$shell" -c "$*"
fi
echo "fake bws: unexpected command $*" >&2
exit 64
FAKE
chmod +x "$WORK/bin/bws"
for tool in bash env awk mktemp cat printf dirname; do
	ln -sf "$(command -v "$tool")" "$WORK/nobws/$tool"
done

# Runs the script with a controlled environment; sets STATUS, OUT (stdout) and ERR (stderr).
run_script() {
	local path="$1"
	shift
	: >"$WORK/log"
	set +e
	env -i HOME="$WORK/home" PATH="$path" FAKE_LOG="$WORK/log" FAKE_SECRET_VALUE="$SECRET_SENTINEL" \
		FAKE_PROJECTS="$DEV_ID\tfrappe-dev\t2026-09-19\n$PRODUCTION_ID\tfrappe-production\t2026-09-19\n" \
		FAKE_SECRETS="$SECRET_ID\tFRAPPE_SECRET_PEPPER\t$SECRET_SENTINEL\t2026-09-19\n" \
		"${EXTRA_ENV[@]}" bash "$SCRIPT" "$@" >"$WORK/out" 2>"$WORK/err"
	STATUS=$?
	set -e
	OUT="$(cat "$WORK/out")"
	ERR="$(cat "$WORK/err")"
	LOG="$(cat "$WORK/log")"
}

fail() {
	echo "  FAIL: $*"
	FAILURES=$((FAILURES + 1))
}
expect_status() { [[ "$STATUS" == "$1" ]] || fail "exit status $STATUS, expected $1 (stderr: $ERR)"; }
expect_contains() { [[ "$1" == *"$2"* ]] || fail "expected to contain '$2' but was: $1"; }
expect_not_contains() { [[ "$1" != *"$2"* ]] || fail "expected not to contain '$2' but was: $1"; }
expect_no_leak() {
	expect_not_contains "$OUT$ERR" "$TOKEN_SENTINEL"
	expect_not_contains "$OUT$ERR" "$SECRET_SENTINEL"
}
test_case() {
	TESTS=$((TESTS + 1))
	echo "- $1"
	EXTRA_ENV=()
}

readonly WITH_BWS="$WORK/bin:/usr/bin:/bin"
readonly WITHOUT_BWS="$WORK/nobws"

test_case "help prints usage and exits 0"
run_script "$WITH_BWS" --help
expect_status 0
expect_contains "$OUT" "Usage: scripts/with-secrets.sh"
expect_contains "$OUT" "frappe-dev"

test_case "no command is a usage error"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS"
expect_status 2
expect_contains "$ERR" "Usage: scripts/with-secrets.sh"

test_case "unknown option is a usage error"
run_script "$WITH_BWS" --bogus ./gradlew bootRun
expect_status 2
expect_contains "$ERR" "unknown option: --bogus"

test_case "missing bws explains how to install it and that the local profile needs no Bitwarden"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITHOUT_BWS" ./gradlew bootRun
expect_status 1
expect_contains "$ERR" "bws"
expect_contains "$ERR" "https://github.com/bitwarden/sdk-sm/releases"
expect_contains "$ERR" "./gradlew bootRun"
expect_contains "$ERR" "docs/secrets.md"
expect_no_leak

test_case "missing token explains how to get one and that the local profile needs no Bitwarden"
run_script "$WITH_BWS" ./gradlew bootRun
expect_status 1
expect_contains "$ERR" "BWS_ACCESS_TOKEN"
expect_contains "$ERR" "machine account"
expect_contains "$ERR" "./gradlew bootRun"
expect_contains "$ERR" "docs/secrets.md"
[[ "$LOG" != *"args: project"* && "$LOG" != *"args: run"* ]] || fail "bws was called without a token: $LOG"

test_case "dry run without bws or token reports what is missing and contacts nothing"
run_script "$WITHOUT_BWS" --dry-run ./gradlew bootRun
expect_status 0
expect_contains "$OUT" "bws: not found"
expect_contains "$OUT" "BWS_ACCESS_TOKEN: not set"
expect_contains "$OUT" "project: frappe-dev"
expect_contains "$OUT" "https://vault.bitwarden.eu"
expect_contains "$OUT" "./gradlew bootRun"

test_case "dry run with token never prints the token and never calls Bitwarden"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS" --dry-run --project frappe-production ./gradlew bootRun
expect_status 0
expect_contains "$OUT" "bws: bws 2.1.0"
expect_contains "$OUT" "BWS_ACCESS_TOKEN: set"
expect_contains "$OUT" "project: frappe-production"
expect_no_leak
[[ "$LOG" != *"args: project"* && "$LOG" != *"args: run"* ]] || fail "dry run contacted Bitwarden: $LOG"

test_case "runs the command with the default project's secrets through the EU profile without a state file"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL" BWS_SERVER_URL="https://vault.bitwarden.com" BWS_UUIDS_AS_KEYNAMES=true)
# shellcheck disable=SC2016 # expanded by the child, not here
run_script "$WITH_BWS" bash -c 'test -n "$FAKE_SECRET" && test -z "${BWS_ACCESS_TOKEN:-}" && echo injected'
expect_status 0
expect_contains "$OUT" "injected"
expect_contains "$LOG" "args: run --project-id $DEV_ID --shell bash --"
expect_contains "$LOG" "BWS_CONFIG_FILE=$(dirname "$SCRIPT")/bws.toml"
expect_contains "$LOG" "BWS_PROFILE=frappe-eu"
expect_not_contains "$LOG" "BWS_SERVER_URL=https"
expect_not_contains "$LOG" "BWS_UUIDS_AS_KEYNAMES=true"
expect_no_leak

test_case "arguments with spaces and quotes reach the command unchanged"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
# shellcheck disable=SC2016 # a literal $ must survive
run_script "$WITH_BWS" printf '[%s]' "two words" "it's" '$HOME'
expect_status 0
[[ "$OUT" == "[two words][it's][\$HOME]" ]] || fail "arguments changed: $OUT"

test_case "an argument containing a newline reaches the command unchanged"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS" printf '[%s]' $'line one\nline two'
expect_status 0
[[ "$OUT" == $'[line one\nline two]' ]] || fail "newline argument changed: $OUT"

test_case "the committed bws profile pins the EU server and opts out of the state file"
profile="$(cat "$(dirname "$SCRIPT")/bws.toml")"
expect_contains "$profile" "[profiles.frappe-eu]"
expect_contains "$profile" 'server_base = "https://vault.bitwarden.eu"'
expect_contains "$profile" 'state_opt_out = "true"'

test_case "an empty project name is a usage error"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS" --project "" true
expect_status 2
expect_contains "$ERR" "--project needs a name"
[[ "$LOG" != *"args: project"* && "$LOG" != *"args: run"* ]] || fail "called Bitwarden with an empty project: $LOG"

test_case "a secret named like a variable that controls processes is refused before running"
for reserved in PATH BASH_ENV LD_PRELOAD JAVA_TOOL_OPTIONS SPRING_APPLICATION_JSON BWS_ACCESS_TOKEN; do
	EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL"
		FAKE_SECRETS="$SECRET_ID\t$reserved\t$SECRET_SENTINEL\t2026-09-19\n")
	run_script "$WITH_BWS" true
	expect_status 1
	expect_contains "$ERR" "$reserved"
	expect_contains "$ERR" "reserved"
	[[ "$LOG" != *"args: run"* ]] || fail "ran with reserved secret $reserved: $LOG"
	expect_no_leak
done
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS" true
expect_status 0
expect_contains "$LOG" "args: secret list $DEV_ID"

test_case "project is selectable by option and by FRAPPE_SECRETS_PROJECT"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS" --project frappe-production true
expect_status 0
expect_contains "$LOG" "--project-id $PRODUCTION_ID"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL" FRAPPE_SECRETS_PROJECT=frappe-production)
run_script "$WITH_BWS" true
expect_status 0
expect_contains "$LOG" "--project-id $PRODUCTION_ID"

test_case "a project the token cannot read fails with guidance"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS" --project frappe-archive true
expect_status 1
expect_contains "$ERR" "frappe-archive"
expect_contains "$ERR" "machine account"
[[ "$LOG" != *"args: run"* ]] || fail "ran without a resolved project: $LOG"
expect_no_leak

test_case "the command's exit code is propagated"
EXTRA_ENV=(BWS_ACCESS_TOKEN="$TOKEN_SENTINEL")
run_script "$WITH_BWS" bash -c 'exit 7'
expect_status 7

echo
if ((FAILURES > 0)); then
	echo "with-secrets.sh: $FAILURES failure(s) in $TESTS tests"
	exit 1
fi
echo "with-secrets.sh: $TESTS tests passed"
