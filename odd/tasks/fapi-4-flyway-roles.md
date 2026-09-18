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
- [x] T1 (67bfd3e) Bootstrap roles: init SQL shared by compose and Testcontainers; test that both roles exist and `frappe_app` cannot create objects (RED → GREEN)
- [x] T2 (525fecf) Flyway as owner, app datasource as `frappe_app`; first migration creates `platform`; test schema + history row
- [x] T3 (5e59e0a) Default privileges: test that a table created by a later migration (test-only migration) is DML-accessible to `frappe_app` and DDL is rejected
- [x] T4 Document the convention in `.agents/skills/writing-code` persistence reference; README local setup if needed
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
- T3 RED: `DefaultPrivilegesIntegrationTests` 5/5 failed (`schema "fixture" does not exist`). GREEN: 5/5 pass after the grant-free test migration `src/test/resources/db/migration/fixture/V202609181946__create_fixture_probe.sql`: CRUD as `frappe_app` succeeds; create/alter/drop/truncate rejected. The privileges themselves were already in the T1 bootstrap, so RED here is the missing later table, not missing grants.
- T4 docs only (persistence + integration-tests references, README). Boot check on a clean volume (`COMPOSE_PROJECT_NAME=frappe-fapi-4`, then `down -v`): app started, history row `platform/V202609181945__create_platform_schema.sql` installed by `frappe_owner`, schema `platform` owned by `frappe_owner`.
- T5 partial: `FRAPPE_TEST_DB=frappe_fapi_4 ./gradlew cleanTest check` BUILD SUCCESSFUL, 15 tests, 0 failures. PR pending (human).
- Review fix 3 RED: `DatabaseRolesIntegrationTests.appRoleCannotRunDdl[3]` (`create temporary table`) failed: the temp table was created. GREEN 5/5 after the script revokes `temporary` on the database and `all` on schema `public` from `public`. Script made idempotent (`\gexec` guarded by `pg_roles`); re-ran twice in the test container, exit 0.

## Next step
Review, push and PR per template (human decision).
