set working-directory := "../.."

infra := "postgres nats valkey otel-collector loki tempo mimir grafana"

# Start the infrastructure and LGTM, without the Frappé hosts
up:
    docker compose -f deploy/compose.yaml up -d {{ infra }}
