# Persistence

Package: `com.frappe.<module>.infrastructure.persistence`. For generic Postgres guidance load `supabase-postgres-best-practices`.

## JPA entities

- Separate `XxxEntity` classes; the domain never sees them.
- MapStruct mapper `XxxMapper` converts entity ↔ aggregate via `Aggregate.reconstitute(...)`.
- `@Version long version` on the root entity; a concurrent update surfaces as a conflict, mapped to `409` on the web.
- Repository adapter implements the domain port and wraps Spring Data repositories.

## Schema per module

- Each module owns one Postgres schema named after it (`identity`, `organization`, …). Entities declare `@Table(schema = "<module>")`.
- No foreign keys or joins across schemas. Store the other module's ID as a plain `uuid` column.

## Flyway

- Migrations under `src/main/resources/db/migration/<module>/`, `V<yyyyMMddHHmm>__<description>.sql`.
- Forward-only; never edit a merged migration. Add a new one.
- `uuid` primary keys (UUIDv7 from the domain), `timestamptz` for instants, `bigint` minor units + `char(3)` currency for money.

## Tenancy and RLS

- Every tenant-scoped table has `tenant_id uuid not null` and an index whose leading column is `tenant_id`.
- Enable and force RLS, with a policy on the session setting:

```sql
alter table tabs enable row level security;
alter table tabs force row level security;
create policy tenant_isolation on tabs
  using (tenant_id = current_setting('app.tenant_id')::uuid);
```

- The application sets `app.tenant_id` per transaction (`set local`) from the authenticated session; the app role is not the table owner and has no `bypassrls`.
- Every new tenant-scoped table ships with a test proving another tenant's rows are invisible.
