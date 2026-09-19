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

- Migrations under `src/main/resources/db/migration/<module>/`, `V<yyyyMMddHHmm>__<description>.sql`. One Flyway instance and one history table (`platform.flyway_schema_history`) for the whole database.
- Review rule: reject a migration outside `db/migration/<module>/`, or with any unqualified object name. Every DDL names its schema (`create table ordering.tab`, never `create table tab`), and only touches the module's own schema.
- A module's first migration creates its schema (`create schema if not exists <module>;`); add the schema to `spring.flyway.schemas` when the module lands.
- Test-only fixture migrations (`src/test/resources/db/migration/<fixture>/`) are exempt from `spring.flyway.schemas`; they exist only in the test database.
- Nothing in the database enforces the folder or qualified-name rule: `frappe_owner` may create any schema. The rule is enforced in review.
- Forward-only; never edit a merged migration. Add a new one.
- `uuid` primary keys (UUIDv7 from the domain), `timestamptz` for instants, `bigint` minor units + `char(3)` currency for money.

## Roles

- `frappe_owner` runs Flyway (`spring.flyway.user/password`) and owns every schema and table. `frappe_app` is the runtime role (`spring.datasource.username/password`): `USAGE` on schemas and DML on tables, never DDL, never an owner.
- Both are created by `docker/postgres/initdb/01-frappe-roles.sh` (compose and Testcontainers), which also sets `alter default privileges for role frappe_owner`, so new schemas, tables and sequences are usable by `frappe_app` without a `GRANT` in the migration. Do not add grants to migrations; change the bootstrap script instead.

## Tenancy and RLS

- Every tenant-scoped table has `tenant_id uuid not null` and an index whose leading column is `tenant_id`.
- Enable and force RLS, with a policy on the transaction-local setting `app.tenant_id` for reads and writes (`using` and `with check`):

```sql
create index tabs_tenant_id_idx on ordering.tabs (tenant_id);
alter table ordering.tabs enable row level security;
alter table ordering.tabs force row level security;
create policy tenant_isolation on ordering.tabs
  using (tenant_id = (select nullif(current_setting('app.tenant_id', true), '')::uuid))
  with check (tenant_id = (select nullif(current_setting('app.tenant_id', true), '')::uuid));
```

- Keep the template exactly: `missing_ok` (`true`) and `nullif(..., '')` make "no tenant" show no rows instead of failing, because a pooled connection that once ran `set_config(..., true)` returns `''` afterwards, not null; `(select ...)` evaluates the setting once per statement, not per row.
- The tenant is bound through the kernel port `com.frappe.platform.TenantScope` (`callAs`/`runAs`), around the use case call and before its transaction starts. While a tenant is bound, every new transaction begins with `select set_config('app.tenant_id', ?, true)` (bind parameter, transaction-local, set by the platform's `TenantTransactionListener`), so a pooled connection carries nothing to its next use. No tenant bound: nothing is set and tenant-scoped tables show no rows. Binding another tenant inside a bound scope, or binding inside a running transaction, throws `IllegalStateException`. The tenant comes from a verified source only: the request path after the membership check (FAPI-46), the event, or the entity a task processes; never from unverified client input.
- The app role is not the table owner and has no `bypassrls`. Every use case has a transaction for the setting: `@CommandUseCase` operations are read-write and `@QueryUseCase` operations read-only `@Transactional`, both enforced by `UseCaseArchitectureTests` (`use-cases.md`).
- Tables that must be found before a tenant exists (identity: people, sessions, pairing) are deliberately not tenant-scoped; record every such exception in the Decision Log.
- Every new tenant-scoped table ships with a test proving another tenant's rows are invisible.
