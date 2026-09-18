# Database

Generic Postgres rules (indexes, query plans, locking, bloat, RLS performance): load skill `supabase-postgres-best-practices`. Below are Frappé specifics. Guidance, not measured facts.

## Diagnose

- `EXPLAIN (ANALYZE, BUFFERS)` the real query as the application role with `app.tenant_id` set, so RLS predicates are included.
- `pg_stat_statements` for top queries by total time.

## RLS

- The policy `tenant_id = current_setting('app.tenant_id')::uuid` is added to every query: every tenant-scoped index leads with `tenant_id` (e.g. `(tenant_id, branch_id, created_at)`).
- Keep policy expressions simple and non-volatile; no subqueries or function calls per row in policies.

## Outbox and inbox

- Outbox (event publication registry): index on completion state / publication date so the relay finds unpublished rows cheaply.
- Delete or archive completed publications on a schedule; unbounded growth slows the relay and vacuum.
- Inbox tables: unique index on `event_id`; purge entries older than the redelivery window.

## Connection pool

- Hikari pool size is bounded by Postgres `max_connections` across all instances; start small (roughly cores × 2 per instance) and measure.
- With virtual threads, the pool is the concurrency limit: watch `hikaricp.connections.pending` and acquisition time before raising it.
- Set statement and lock timeouts so a slow query cannot hold connections indefinitely.

## Schema

- One schema per module keeps tables small and indexes focused; never add cross-schema joins to "speed up" a read. Build a read model from events instead.
