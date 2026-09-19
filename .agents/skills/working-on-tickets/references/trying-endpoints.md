# Trying endpoints by hand

Every ticket that adds or changes an HTTP route also runs the real application and calls the route by hand before the PR opens. Tests prove the contract. A manual call shows what a client actually gets: status, headers, localized problem body, mail in Mailpit, telemetry in Grafana.

## Who and when

- The writer runs it after `./gradlew check` is green and before reporting.
- The orchestrator reruns one call as a spot check before opening the PR.
- Tickets without route changes write "Not applicable: no route changes" in the PR.

## Run the app from the worktree

Shared services start once from the main checkout (`running-in-parallel.md`). Each ticket gets its own database and port, so parallel tickets never share migrations or collide.

```bash
N=12   # ticket number
# Database per ticket, with the frappe_owner/frappe_app grants (idempotent script)
docker compose -f ~/Projects/nulled/frappe-api/compose.yaml exec -T -e POSTGRES_DB=frappe_try_$N postgres \
  sh -c 'createdb -U "$POSTGRES_USER" "$POSTGRES_DB" 2>/dev/null; /docker-entrypoint-initdb.d/01-frappe-roles.sh'
# App on port 18000+N against that database (run in the background; logs to a scratch file)
FRAPPE_DB_URL=jdbc:postgresql://localhost:${FRAPPE_POSTGRES_PORT:-5432}/frappe_try_$N SERVER_PORT=$((18000+N)) \
  ./gradlew -q bootRun > /tmp/fapi-$N-boot.log 2>&1 &
until curl -sf localhost:$((18000+N))/actuator/health >/dev/null; do sleep 2; done
```

When another project holds host port 5432, export `FRAPPE_POSTGRES_PORT` (e.g. `15432`) for both compose and the app (README, "Run locally").

## What to call

Use `curl -i` so the status and headers show. For every added or changed route:

- **Happy path:** the documented request, and the answer's status, body and `Location` if it applies.
- **Each refusal the ticket defines:** one call per problem slug; check the `code`.
- **Authentication:** without credentials (401), and with another person's credentials where ownership applies.
- **Localization:** `Accept-Language: es` and `en`; the problem `title`/`detail` change and `Content-Language` matches.
- **Side effects:**
  - mail in Mailpit (<http://localhost:8025>);
  - rows in the ticket database (`psql` as `frappe_app`);
  - the declared business metric and span in Grafana (<http://localhost:3000>), or `/actuator/metrics/<name>`.

Scalar (<http://localhost:$((18000+N))/scalar>, `local` profile) is fine for exploring. The evidence is the curl transcript.

## Evidence in the PR

Under **Verification → Manual calls**, one line per call: `METHOD path (headers that matter) → status, code / key body fields`. Add one line on side effects (mail subject, metric increment). Redact tokens, codes and passwords.

## Clean up

```bash
fuser -k $((18000+N))/tcp
docker compose -f ~/Projects/nulled/frappe-api/compose.yaml exec -T postgres dropdb -U frappe --if-exists frappe_try_$N
```
