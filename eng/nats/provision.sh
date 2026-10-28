#!/usr/bin/env bash
# Creates or updates the JetStream topology the deployed processes only verify (NatsProvisioning.Verify):
# the FRAPPE_EVENTS stream and one durable pull consumer per consumed integration event. Run it with
# administrator credentials before deploying a release that adds an event or a consumer; it is idempotent.
#
# Usage: provision.sh <event identity>...   e.g. provision.sh order.tab.closed.v1 kitchen.ticket.opened.v1
# Each identity is an event's [MessageIdentity]; the names follow NatsTopology in Frappe.Platform.Messaging:
# subject <root>.<identity>, consumer <prefix>-<identity with dots as dashes>.
#
# Environment: NATS_URL and NATS_CREDS (a credentials file) as the nats CLI reads them, and
# FRAPPE_NATS_STREAM, FRAPPE_NATS_SUBJECT_ROOT, FRAPPE_NATS_CONSUMER_PREFIX, FRAPPE_NATS_REPLICAS, which
# must match the processes' Nats:Stream, Nats:SubjectRoot, Nats:ConsumerPrefix and Nats:Replicas.
set -euo pipefail

stream="${FRAPPE_NATS_STREAM:-FRAPPE_EVENTS}"
root="${FRAPPE_NATS_SUBJECT_ROOT:-frappe}"
prefix="${FRAPPE_NATS_CONSUMER_PREFIX:-frappe}"
replicas="${FRAPPE_NATS_REPLICAS:-3}"

# The limits match NatsTopology; max_msg_size is the one Wolverine cannot declare. Retention and storage
# are fixed when the stream is created.
stream_limits=(
  --subjects "${root}.>" --discard old --no-allow-direct --no-allow-rollup
  --max-age 30d --max-bytes 10GiB --max-msg-size 1MiB --max-msgs=-1 --max-msgs-per-subject=-1
  --dupe-window 15m --replicas "$replicas"
)

if nats stream info "$stream" > /dev/null 2>&1; then
  nats stream edit "$stream" "${stream_limits[@]}" --force
else
  nats stream add "$stream" --retention limits --storage file "${stream_limits[@]}" --defaults
fi

config="$(mktemp)"
trap 'rm -f "$config"' EXIT

for identity in "$@"; do
  if [[ ! "$identity" =~ ^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*\.v[0-9]+$ ]]; then
    echo "error: '$identity' is not an event identity (module.entity.fact.vN)" >&2
    exit 1
  fi

  consumer="${prefix}-${identity//./-}"
  # Deliver policy cannot change once a consumer exists, so an existing consumer is left as it is.
  if nats consumer info "$stream" "$consumer" > /dev/null 2>&1; then
    echo "consumer $consumer exists"
    continue
  fi

  # Unlimited deliveries never skip an event; the backoff spaces redeliveries while the inbox is down. Its
  # first step equals AckWait (30s), which the server takes as the consumer's ack wait. Given as JSON: the
  # CLI's --backoff flags replace max_deliver -1 with a finite count.
  cat > "$config" <<JSON
{
  "durable_name": "${consumer}",
  "filter_subject": "${root}.${identity}",
  "deliver_policy": "new",
  "ack_policy": "explicit",
  "replay_policy": "instant",
  "ack_wait": 30000000000,
  "max_deliver": -1,
  "backoff": [30000000000, 60000000000, 120000000000, 300000000000, 600000000000]
}
JSON
  nats consumer add "$stream" "$consumer" --config "$config"
done
