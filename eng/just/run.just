set working-directory := "../.."
set shell := ["bash", "-euo", "pipefail", "-c"]

# Run the Api on http://localhost:5080 (needs `just infra up` and `just db migrate`)
api:
    ASPNETCORE_URLS=http://localhost:5080 \
    ConnectionStrings__Valkey="localhost:${VALKEY_PORT:-6379}" \
    Http__Console__Origins="${FRAPPE_CONSOLE_ORIGINS:-http://localhost:5173}" \
    dotnet run --project src/Hosts/Frappe.Api

# Run the Worker as Development (needs `just infra up` and `just db migrate`)
worker:
    DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}" \
    ConnectionStrings__Scheduler="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_scheduler;Password=$FRAPPE_SCHEDULER_PASSWORD" \
    ConnectionStrings__Valkey="localhost:${VALKEY_PORT:-6379}" \
    ConnectionStrings__Nats="nats://localhost:${NATS_PORT:-4222}" \
    dotnet run --project src/Hosts/Frappe.Worker
