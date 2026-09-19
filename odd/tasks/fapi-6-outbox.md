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
- [x] T3 ARCHIVE: acked event moves to `platform.event_publication_archive`
- [x] T4 Scheduled resubmission of failed/stale publications: NATS down → recovers → published once and archived
- [x] T5 Docs (`writing-code` events reference: how a handler publishes), `./gradlew check` green, PR per template

- [x] T6 Review MAJOR 1: stuck detection by latest attempt (`last_resubmission_date`, else `publication_date`), Modulith staleness monitor off, dedup claim qualified
- [x] T7 Review MAJOR 2: `max-attempts`, dead-letter table, ERROR log once, gauge, manual replay docs, no starvation, retry backoff
- [x] T8 Review MAJOR 3: unreadable rows (unknown type, unreadable payload) never block a batch
- [x] T9 Review minors: `frappe.outbox.batch_size` field, integration-tests.md outbox assertion rule, maxInFlight comment; full check

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
- T3 RED: `OutboxArchiveIntegrationTests.acknowledgedPublicationMovesToTheArchive` failed with `ConditionTimeoutException` (the property already shipped with T1; RED observed by removing it locally, so the default UPDATE mode kept the row in `event_publication`). GREEN with `spring.modulith.events.completion-mode=archive`; full suite green.
- T4 RED: `OutboxRecoveryIntegrationTests.failedPublicationIsResubmittedOnceNatsRecoversAndArchived` and `stalePublicationIsMarkedFailedThenResubmittedAndArchived` timed out (`ConditionTimeoutException`, nothing resubmits while the NATS connection survives a pause); `FailedPublicationResubmitterTest` did not compile. GREEN: `FailedPublicationResubmitter` (scheduled fixed delay, `FailedEventPublications.resubmit` with batch size = max in flight, WARN with `frappe.outbox.recovery_interval` on `DataAccessException`), `OutboxRecoveryProperties` (`frappe.outbox.recovery.interval=1m`, `batch-size=100`, validated at startup), `@EnableScheduling` activating the staleness monitor, staleness 1m for all three statuses. One interim failure (2 messages on the subject) came from FAILED rows of earlier runs in the reused database being delivered too; the test now counts by `Nats-Msg-Id`. Full `check --rerun-tasks`: 53 tests, 0 failures.
- T5: `writing-code` `domain-events.md` (publishing through `DomainEventPublisher`, archive, three recovery paths, config) and `use-cases.md` handler example updated. `FRAPPE_TEST_DB=frappe_fapi_6 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 53 tests, 0 failures. PR not opened (orchestrator/human decision).

### Review round 1 (one approve, one changes requested)
- T6 finding (2.1.1 sources): `DefaultEventPublicationRegistry.markFailed(Status, Staleness)` filters `getPublicationDate().isBefore(now - staleness)` for every status, and `markResubmitted` sets `LAST_RESUBMISSION_DATE` and increments `COMPLETION_ATTEMPTS`. Fix: staleness properties removed (monitor off, all durations zero); `OutboxRecoveryRepository.releaseStuckPublications` sets FAILED where status in PUBLISHED/PROCESSING/RESUBMITTED and `coalesce(last_resubmission_date, publication_date) < now - stuck-after` (default 5m). RED: `StuckPublicationIntegrationTests` did not compile (no repository); GREEN: `inFlightResubmissionOfAnOldEventIsNotReleased`, `resubmissionWithoutOutcomeAfterTheThresholdIsReleasedAsFailed`, `firstAttemptWithoutOutcomeIsJudgedByItsPublicationDate`, `FailedPublicationResubmitterTest.releasesAttemptsStartedBeforeTheStuckThresholdBeforeResubmitting`, `OutboxRecoveryIntegrationTests` (now `stuck-after=2s`) all pass.
- T7 finding (2.1.1 sources): `JdbcEventPublicationRepositoryV2.findFailedPublications` orders by `PUBLICATION_DATE` and applies `LIMIT` in SQL; `ResubmissionOptions.getFilter()` is applied afterwards in `DefaultEventPublicationRegistry.processPublications`, so filtering exhausted rows there would let them fill every batch. `IncompleteEventPublications.resubmitIncompletePublications(Predicate)` reads all incomplete rows with no limit and still guards each row with `markResubmitted` (`STATUS != 'RESUBMITTED'`, increments `COMPLETION_ATTEMPTS`). Fix: selection in our SQL (`findRetryable`: FAILED, backoff `interval * 2^(attempts-1)` capped at `max-backoff`, least recently attempted first, limit = batch minus in flight), resubmitted through that predicate by id; exhausted rows moved in one statement to the new `platform.event_publication_dead_letter` (migration `V202609182100`), each logged once at ERROR with ECS fields; gauge `frappe.outbox.dead.letters` (`DeadLetterMetrics`, a `MeterBinder` refreshed per run); `max-attempts` (24) and `max-backoff` (1h) validated at startup; manual replay SQL in `domain-events.md`. RED: `DeadLetterIntegrationTests` and the rewritten `FailedPublicationResubmitterTest` did not compile; the first GREEN attempt failed with `BadSqlGrammarException` (untyped parameters) and then on leftover 2100-dated rows of earlier `StuckPublicationIntegrationTests` runs (deleted once; both test classes now clean their rows in `@AfterEach`). GREEN: `exhaustedPublicationMovesToTheDeadLetterTable`, `publicationsInBackoffNeverStarveANewerFailure`, `leastRecentlyAttemptedPublicationIsRetriedFirst`, `resubmitsOnlyTheSelectedPublications`, `publicationsStillInFlightReduceTheBatch`, `aFullWindowOfInFlightPublicationsSkipsTheRun`, `everyDeadLetterIsLoggedOnceAtErrorWithItsContext`, `theDeadLetterGaugeReportsTheStoredCount`, full suite green.
- T8 finding (2.1.1 sources): `JdbcEventPublicationRepositoryV2.loadClass` returns null on `ClassNotFoundException` (WARN) and the row is dropped from every read, so it is never attempted and would occupy our retry selection forever; `JacksonEventSerializer.deserialize` throws Jackson 3 `JacksonException` lazily from `getEvent()`, inside Modulith's per-row catch when invoked by the listener (row left RESUBMITTED), but from the filter predicate it aborts the whole stream (the reconnect path's `externalization.supports(getEvent())`). Fix: failed event types checked with `ClassUtils.isPresent` and dead-lettered as `UNKNOWN_EVENT_TYPE`; the resubmission predicate deserializes selected rows first and dead-letters `JacksonException` rows as `UNREADABLE_PAYLOAD`; `NatsConnectSetup` skips them. No `RuntimeException` catch needed; rule added to `errors.md`. RED: `OutboxRecoveryIntegrationTests.unreadableRowsBecomeDeadLettersWhileAValidFailureIsStillRecovered` timed out waiting for the dead letters, the new `FailedPublicationResubmitterTest` cases did not compile, `NatsConnectSetupTest.anUnreadablePublicationDoesNotStopTheResubmissionOnReconnect` failed with `StreamReadException`. GREEN: all pass, full suite green.
- T9: `frappe.outbox.batch_size` log field (landed with T6), maxInFlight/batch-size comment at the call site (`resubmitDueFailures`), outbox assertion rule in `testing-code/references/integration-tests.md`. `FRAPPE_TEST_DB=frappe_fapi_6 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 68 tests, 0 failures, 0 errors (run twice).

## Next step
Review, then PR per template (human decision).
