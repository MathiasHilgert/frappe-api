#!/usr/bin/env bash
# Creates the verified dev owner described in scripts/dev/seed-owner.env through the running API's public endpoints,
# reading the verification code from Mailpit. Safe to run again: an existing owner is verified or reused, never reset.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$here/seed-owner.env"
api="${FRAPPE_API:-http://localhost:5131}"
mailpit="${MAILPIT:-http://localhost:8025}"
identity="$api/v1/identity"

curl -fsS "$api/health/live" >/dev/null || { echo "The API is not running at $api; start it with 'just dev'." >&2; exit 1; }
curl -fsS "$mailpit/api/v1/info" >/dev/null || { echo "Mailpit is not running at $mailpit; start it with 'just dev'." >&2; exit 1; }

body_file="$(mktemp)"; trap 'rm -f "$body_file"' EXIT
post() { # post <path> <json> -> prints "<status> <body>" on one line
  local status
  status="$(curl -sS -o "$body_file" -w '%{http_code}' -H 'Content-Type: application/json' -d "$2" "$identity$1")"
  echo "$status $(tr -d '\n' <"$body_file")"
}

newest_mail() { # "<id> <subject>" of the newest mail Mailpit holds for the owner, or nothing
  curl -fsS "$mailpit/api/v1/search?query=$(jq -rn --arg q "to:$DEV_OWNER_EMAIL" '$q|@uri')&limit=1" \
    | jq -r '.messages[0] // empty | "\(.ID) \(.Subject)"'
}

code_after() { # waits for a mail newer than <id> (the outbox delivers after commit) and prints its 6-digit code
  local before="$1" mail
  for _ in $(seq 1 30); do
    mail="$(newest_mail)"
    if [[ -n "$mail" && "${mail%% *}" != "$before" ]]; then grep -oE '[0-9]{6}' <<<"${mail#* }" | tail -n1; return 0; fi
    sleep 1
  done
  echo "No verification mail reached Mailpit for $DEV_OWNER_EMAIL." >&2; return 1
}

credentials="$(jq -n --arg e "$DEV_OWNER_EMAIL" --arg p "$DEV_OWNER_PASSWORD" '{email:$e,password:$p}')"
read -r status body < <(post /sessions "$credentials")
case "$status" in
  201) echo "Dev owner already exists and is verified." ;;
  401|403)
    owner_id=""
    if [[ "$status" == 403 ]]; then owner_id="$(jq -r '.ownerId // empty' <<<"$body")"; fi
    before="$(newest_mail | cut -d' ' -f1)"
    if [[ -z "$owner_id" ]]; then
      read -r status body < <(post /owners "$(jq -n --arg n "$DEV_OWNER_NAME" --arg e "$DEV_OWNER_EMAIL" \
        --arg p "$DEV_OWNER_PASSWORD" '{name:$n,email:$e,password:$p}')")
      [[ "$status" == 201 ]] || { echo "Registration failed ($status): $body" >&2; exit 1; }
      owner_id="$(jq -r '.id' <<<"$body")"
    else
      post "/owners/$owner_id/verification-codes" '{}' >/dev/null
    fi
    code="$(code_after "$before")"
    read -r status body < <(post "/owners/$owner_id/verification" "$(jq -n --arg c "$code" '{code:$c}')")
    [[ "$status" == 201 ]] || { echo "Verification failed ($status): $body" >&2; exit 1; }
    echo "Dev owner created and verified." ;;
  *) echo "Unexpected sign-in answer ($status): $body" >&2; exit 1 ;;
esac

echo "Sign in with: $DEV_OWNER_EMAIL / $DEV_OWNER_PASSWORD (from scripts/dev/seed-owner.env)"
