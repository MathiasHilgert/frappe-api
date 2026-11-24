set working-directory := "../.."
set shell := ["bash", "-euo", "pipefail", "-c"]

# Run the Api on http://localhost:5080 (needs `just infra up` and `just db migrate`)
api:
    ASPNETCORE_URLS=http://localhost:5080 \
    ConnectionStrings__Valkey="localhost:${VALKEY_PORT:-6379}" \
    ConnectionStrings__Identity="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_identity;Password=$FRAPPE_IDENTITY_PASSWORD" \
    ConnectionStrings__Notifications="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_notifications;Password=$FRAPPE_NOTIFICATIONS_PASSWORD" \
    Notifications__AddressHash__CurrentKeyId=1 \
    Notifications__AddressHash__Keys__1="$FRAPPE_NOTIFICATIONS_ADDRESS_HASH_KEY" \
    Notifications__Email__Resend__WebhookSecret="$FRAPPE_NOTIFICATIONS_WEBHOOK_SECRET" \
    Notifications__Unsubscribe__SigningKey="$FRAPPE_NOTIFICATIONS_UNSUBSCRIBE_KEY" \
    Http__Console__Origins="${FRAPPE_CONSOLE_ORIGINS:-http://localhost:5173}" \
    dotnet run --project src/Hosts/Frappe.Api

# Run the Worker as Development (needs `just infra up` and `just db migrate`)
worker:
    DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}" \
    ConnectionStrings__Scheduler="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_scheduler;Password=$FRAPPE_SCHEDULER_PASSWORD" \
    ConnectionStrings__Identity="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_identity_worker;Password=$FRAPPE_IDENTITY_WORKER_PASSWORD" \
    ConnectionStrings__Notifications="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=frappe;Username=frappe_notifications_worker;Password=$FRAPPE_NOTIFICATIONS_WORKER_PASSWORD" \
    ConnectionStrings__Valkey="localhost:${VALKEY_PORT:-6379}" \
    ConnectionStrings__Nats="nats://localhost:${NATS_PORT:-4222}" \
    Notifications__AddressHash__CurrentKeyId=1 \
    Notifications__AddressHash__Keys__1="$FRAPPE_NOTIFICATIONS_ADDRESS_HASH_KEY" \
    Notifications__Unsubscribe__SigningKey="$FRAPPE_NOTIFICATIONS_UNSUBSCRIBE_KEY" \
    Notifications__Unsubscribe__PageUrl="$FRAPPE_NOTIFICATIONS_UNSUBSCRIBE_PAGE_URL" \
    Notifications__Links__ConsoleUrl="$FRAPPE_NOTIFICATIONS_CONSOLE_URL" \
    Notifications__Unsubscribe__ApiBaseUrl="$FRAPPE_NOTIFICATIONS_API_URL" \
    Notifications__Email__Sender="${Notifications__Email__Sender:-Logging}" \
    dotnet run --project src/Hosts/Frappe.Worker
