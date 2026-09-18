# FAPI-4 — platform: Migrate module schemas with Flyway under least-privilege roles

Plane: [FAPI-4](https://app.plane.so/nulled-software/browse/FAPI-4/) (module platform, size M, sensitive: database grants). Branch: `feat/fapi-4-flyway-roles`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-4`.

## Objective
Give every module a place to persist data: one Flyway instance, one schema per module, and least-privilege roles from day one.

## Decisions
- Single Flyway instance, locations `classpath:db/migration` with one folder per module (`db/migration/<module>/`), one history table.
- Naming `V<yyyyMMddHHmm>__<description>.sql`; DDL always schema-qualified.
- First migration creates schema `platform`.
- Roles: `frappe_owner` runs Flyway and owns schemas/tables; `frappe_app` is the runtime role with `USAGE` on module schemas and DML only. `ALTER DEFAULT PRIVILEGES` so later tables grant DML to `frappe_app`.
- Flyway connects as owner (`spring.flyway.user/password`), the app as `frappe_app` (`spring.datasource.*`).
- Roles are created by the database bootstrap (compose init script and Testcontainers init script), not by Flyway, since Flyway runs as the owner.

## Out of scope
RLS and `tenant_id`; the outbox tables (FAPI-6).

## TDD
Strict TDD. Runner: `./gradlew test` (JUnit 5, Testcontainers Postgres 18, `FRAPPE_TEST_DB=frappe_fapi_4`). RED before each behavior.

## Tasks
- [x] T1 Bootstrap roles: init SQL shared by compose and Testcontainers; test that both roles exist and `frappe_app` cannot create objects (RED → GREEN)
- [x] T2 Flyway as owner, app datasource as `frappe_app`; first migration creates `platform`; test schema + history row
- [ ] T3 Default privileges: test that a table created by a later migration (test-only migration) is DML-accessible to `frappe_app` and DDL is rejected
- [ ] T4 Document the convention in `.agents/skills/writing-code` persistence reference; README local setup if needed
- [ ] T5 `./gradlew check` green; PR per template

## Acceptance (from ticket)
- Empty DB → startup creates `platform` and records the migration.
- `frappe_app` cannot run DDL in any schema.
- Tables from later migrations are DML-accessible to `frappe_app` without extra GRANT.
- Migrations outside `db/migration/<module>/` or unqualified are rejected by documented rule.

## Checks
`./gradlew check`; `docker compose down -v && docker compose up -d` then app boot.

## Progress / evidence
- T1 RED: `DatabaseRolesIntegrationTests` 4/4 failed: `bootstrapCreatesOwnerAndAppRoles` (roles absent), `applicationConnectsAsUnprivilegedAppRole` (connected as superuser `test`), `appRoleCannotRunDdl` x2 (DDL succeeded as superuser). GREEN: 4/4 pass after `docker/postgres/initdb/01-frappe-roles.sh`, compose mount, app/Flyway credentials and Testcontainers wiring (no `@ServiceConnection`, it would force the superuser).

- T2 RED: `FlywayMigrationIntegrationTests.startupRecordsPlatformMigrationInSingleHistory` failed (expected size 1 but was 0: no `platform/` migration in history). GREEN: 3/3 pass after `db/migration/platform/V202609181945__create_platform_schema.sql`. Note: `spring.flyway.schemas=platform` makes Flyway create the schema (as owner) to host the history table; the migration is `create schema if not exists`, so it stays the documented first step and is a no-op on that path.

## Next step
T1.
