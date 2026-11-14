set working-directory := "../.."
set shell := ["bash", "-euo", "pipefail", "-c"]

# Run the Api on http://localhost:5080 (needs `just infra up` and `just db migrate`)
api:
    ASPNETCORE_URLS=http://localhost:5080 \
    ConnectionStrings__Geo="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_geo;Password=$FRAPPE_GEO_PASSWORD" \
    ConnectionStrings__Valkey="localhost:${VALKEY_PORT:-6379}" \
    dotnet run --project src/Hosts/Frappe.Api

# Run the Worker (needs `just infra up` and `just db migrate`)
worker:
    ConnectionStrings__Scheduler="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_scheduler;Password=$FRAPPE_SCHEDULER_PASSWORD" \
    ConnectionStrings__Valkey="localhost:${VALKEY_PORT:-6379}" \
    ConnectionStrings__Nats="nats://localhost:${NATS_PORT:-4222}" \
    dotnet run --project src/Hosts/Frappe.Worker
