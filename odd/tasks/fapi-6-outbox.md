# FAPI-6 — platform: Record domain events in the transactional outbox

Plane: [FAPI-6](https://app.plane.so/nulled-software/browse/FAPI-6/) (module platform, size M, sensitive: events). Branch: `feat/fapi-6-outbox`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-6`.

## Objective
Handlers save an aggregate and its domain events in one transaction; events are relayed to NATS (FAPI-5) and archived once acked.

## Decisions
- Switch the Modulith registry from `spring-modulith-starter-jpa` to the JDBC registry; our own entities keep JPA.
- Real Flyway migration in `db/migration/platform/` creating the official Modulith 2.1.1 Postgres tables (`event_publication`, `event_publication_archive`) in schema `platform`, version later than `V202609181950`; registry pointed to schema `platform` (verify the property in 2.1.1 sources).
- Delete the FAPI-5 stopgap `src/test/resources/db/migration/fixture/V202609181950__event_publication_stopgap.sql` and every `hibernate.default_schema=platform` test property; document resetting reused `frappe_fapi_*` test containers.
- `spring.modulith.events.completion-mode=ARCHIVE`.
- `DomainEventPublisher` port in the platform kernel (application-facing), adapter over `ApplicationEventPublisher`; handlers call it after saving the aggregate inside the same transaction. The domain never sees Spring or Modulith.
- Recovery: staleness settings plus a scheduled job resubmitting failed or stale publications (in addition to FAPI-5's resubmit on NATS reconnect); not relying on republish at restart (Modulith issue #526).
- Standards from `writing-code` apply: Javadoc everywhere, dedicated exceptions, ECS log fields, IdGenerator, clean-code rules.

## Out of scope
Archive purge; consumers and inbox; trace propagation (FAPI-8).

## TDD
Strict TDD. Runner: `./gradlew test` (Testcontainers Postgres 18 + per-context NATS, `FRAPPE_TEST_DB=frappe_fapi_6`). RED before each behavior.

## Tasks
- [x] T0 Read Modulith 2.1.1 JDBC registry sources: official Postgres DDL, schema property, archive mode, resubmission API; record here
- [x] T1 Switch to JDBC registry + Flyway migration of both tables in `platform`; remove the stopgap; FAPI-5 tests green on the real tables
- [x] T2 `DomainEventPublisher` port + adapter; commit → one row per event; rollback → no row
- [ ] T3 ARCHIVE: acked event moves to `platform.event_publication_archive`
- [ ] T4 Scheduled resubmission of failed/stale publications: NATS down → recovers → published once and archived
- [ ] T5 Docs (`writing-code` events reference: how a handler publishes), `./gradlew check` green, PR per template

## Acceptance (from ticket)
- Commit → one publication row per event in `platform.event_publication`.
- Rollback → no row.
- Acked → row in `platform.event_publication_archive`.
- NATS down then back → resubmission publishes once and archives.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_6 ./gradlew check`.

## T0 findings (Spring Modulith 2.1.1 sources, Maven Central `-sources.jar`)
- Artifact: `spring-modulith-starter-jdbc` (core, events-api, events-core, events-jackson, events-jdbc). Auto-config `JdbcEventPublicationAutoConfiguration`; default schema version V2 (`use-legacy-structure=false`).
- Official DDL: `org/springframework/modulith/events/jdbc/schemas/v2/schema-postgresql.sql` and `schema-postgresql-archive.sql`. Both tables have identical columns: `id uuid pk, listener_id text, event_type text, serialized_event text, publication_date timestamptz not null, completion_date timestamptz, status text, completion_attempts int, last_resubmission_date timestamptz`; indexes: hash on `serialized_event`, btree on `completion_date` (`*_serialized_event_hash_idx`, `*_by_completion_date_idx`).
- Schema property: `spring.modulith.events.jdbc.schema` (`JdbcConfigurationProperties`); tables become `<schema>.EVENT_PUBLICATION` and `<schema>.EVENT_PUBLICATION_ARCHIVE` (archive only in ARCHIVE mode).
- Schema initialization: `spring.modulith.events.jdbc.schema-initialization.enabled` (metadata default false, but the nested initializer auto-config is `matchIfMissing=true`); set it `false` explicitly, Flyway owns the DDL.
- Completion mode: `spring.modulith.events.completion-mode=ARCHIVE` (`CompletionMode.from(Environment)`, default UPDATE). ARCHIVE copies the row with status COMPLETED into the archive (`INSERT ... SELECT ... WHERE NOT EXISTS`) and deletes it from `event_publication`.
- Resubmission APIs: `IncompleteEventPublications.resubmitIncompletePublications(Predicate | ResubmissionOptions)`, `resubmitIncompletePublicationsOlderThan(Duration)`; `FailedEventPublications.resubmit(ResubmissionOptions)` reads `STATUS = 'FAILED'` rows published before `now - minAge`, limited by `maxInFlight` (counts RESUBMITTED) and `batchSize` (`ResubmissionOptions.defaults()`: unbounded, 100, 0, all).
- Staleness: `spring.modulith.events.staleness.{published,processing,resubmitted,check-interval}` (`StalenessProperties`); monitoring runs only if one duration is non-zero, via `SchedulingConfigurer` (needs `@EnableScheduling`). Pitfall: once enabled, every status whose duration is ZERO is marked failed immediately (`reference = now - 0`), so all three durations must be set.
- `republish-outstanding-events-on-restart` stays off (Modulith issue #526).

## Progress / evidence
- T0 done: findings above.
- T1 RED: `FlywayMigrationIntegrationTests` `startupRecordsPlatformMigrationInSingleHistory`, `startupCreatesTheOutboxTablesOwnedByOwnerRole`, `outboxTablesCarryTheOfficialRegistryIndexes` failed (5 run, 3 failed: no archive table, no outbox migration). GREEN: all 45 tests pass, including FAPI-5 NATS tests on the real tables without `hibernate.default_schema`. The first green attempt hit `FlywayValidateException` on the reused `frappe_fapi_6` container (stopgap already applied); reset documented in `testing-code/references/integration-tests.md`.
- T2 RED: `DomainEventPublisherIntegrationTests` did not compile (no `DomainEventPublisher`). GREEN: `committedTransactionStoresOnePublicationRowPerEvent`, `rolledBackTransactionStoresNoPublicationRow`, `publishingOutsideATransactionFailsFast` pass. Second RED: `publishAll` outside a transaction did not fail (interface default method self-invoked `publish`, bypassing the proxy); fixed with class-level `@Transactional(MANDATORY)` and an explicit `publishAll`. Full suite green.

## Next step
T0.
