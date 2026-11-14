set working-directory := "../.."
set shell := ["bash", "-euo", "pipefail", "-c"]

# Build the image and start everything: infrastructure, Migrator, Api, Worker and LGTM
up:
    docker compose -f deploy/compose.yaml up -d --build
    @echo "Api: http://localhost:${API_PORT:-8080}/docs/ · Grafana: http://localhost:${GRAFANA_PORT:-3000}"

# Stop the stack; volumes (data) are kept
down:
    docker compose -f deploy/compose.yaml down

# Stop the stack and delete every volume: the next `up` starts from an empty database
reset:
    docker compose -f deploy/compose.yaml down -v

# Show the state of every service
ps:
    docker compose -f deploy/compose.yaml ps

# Readiness of the Api (HTTP) and of the Worker (TCP probe), with each critical dependency
health:
    @echo "api:    $(curl -s http://localhost:${API_PORT:-8080}/health/ready)"
    @docker compose -f deploy/compose.yaml exec worker /app/worker/Frappe.Worker --probe tcp://localhost:8082 && echo "worker: ready" || echo "worker: not ready"

# Follow the logs of one service (api, worker, migrator, postgres, ...)
logs service:
    docker compose -f deploy/compose.yaml logs -f {{ service }}
