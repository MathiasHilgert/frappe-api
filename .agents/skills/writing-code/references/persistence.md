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
- Enable and force RLS, with a policy on the session setting:

```sql
alter table tabs enable row level security;
alter table tabs force row level security;
create policy tenant_isolation on tabs
  using (tenant_id = current_setting('app.tenant_id')::uuid);
```

- The application sets `app.tenant_id` per transaction (`set local`) from the authenticated session; the app role is not the table owner and has no `bypassrls`. Every use case has that transaction: command handlers are read-write and query handlers read-only `@Transactional`, both enforced at startup (`use-cases.md`).
- Every new tenant-scoped table ships with a test proving another tenant's rows are invisible.
