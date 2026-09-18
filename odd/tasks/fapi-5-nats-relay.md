# FAPI-5 — platform: Relay domain events to NATS JetStream

Plane: [FAPI-5](https://app.plane.so/nulled-software/browse/FAPI-5/) (module platform, size M, sensitive: events). Branch: `feat/fapi-5-nats-relay`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-5`.

## Objective
Events leave the process through NATS JetStream with at-least-once delivery and deduplication, using a custom Spring Modulith externalization adapter on `jnats`.

## Decisions
- Custom adapter implementing Spring Modulith 2.1.1's externalization SPI, modeled on `spring-modulith-events-kafka` (verify the exact interfaces from source first).
- `io.nats:jnats` 2.26.2; one `Connection` bean, infinite reconnect, graceful close; properties `frappe.nats.*`.
- `DomainEvent` envelope in the platform kernel: `eventId` (UUIDv7, generated once), `occurredAt` (Instant UTC), `aggregateId`, `aggregateVersion` (long), `eventVersion` (int).
- Stream `FRAPPE`, subjects `frappe.>`, created/updated idempotently at startup; retention Limits, max age 7 days, 1 replica, duplicate window 10 minutes.
- Subject `frappe.<module>.<event-kebab>.v<eventVersion>`; synchronous publish with ack and a timeout.
- Headers: `Nats-Msg-Id` = eventId, `Frappe-Event-Type`, `Frappe-Event-Version`, `Frappe-Aggregate-Id`, `Frappe-Aggregate-Version`, `Frappe-Occurred-At`; JSON payload.
- No cross-instance ordering guarantee; consumers use `aggregateVersion`.

## T0 findings (Spring Modulith 2.1.1 and jnats 2.26.2 sources)
- `spring-modulith-events-kafka` 2.1.1 builds an `EventExternalizationTransport` lambda `(payload, target) -> CompletableFuture<?>` and wraps it in `new EventExternalizerModuleListener(configuration, transport)` (default `spring.modulith.events.externalization.mode=module-listener`); an outbox mode uses `OutboxEventExternalizerFactory.forTransport(transport)`.
- Chosen SPI: `org.springframework.modulith.events.support.EventExternalizationTransport` (public, not deprecated) registered through `EventExternalizerModuleListener` (public, `@ApplicationModuleListener(propagation = SUPPORTS)`, so the publication registry marks the publication complete only when the returned future completes normally). `DelegatingEventExternalizer`/`EventExternalizationSupport` are `@Deprecated(since = "2.1", forRemoval = true)`; not used.
- Selection and routing: our own `EventExternalizationConfiguration` bean replaces the `@ConditionalOnMissingBean` default: `externalizing().select(annotatedAsExternalized()).routeAll(...)` computing `frappe.<module>.<event-kebab>.v<n>` from the `DomainEvent`.
- jnats 2.26.2: `Nats.connect(Options)` with `Options.builder().server(..).connectionName(..).maxReconnects(-1)`; `Connection.drain(Duration)` for graceful close; `JetStreamManagement.getStreamInfo/addStream/updateStream` (missing stream → `JetStreamApiException#getErrorCode() == 404`; no named constant in 2.26.2); `StreamConfiguration.builder().name/subjects/retentionPolicy/maxAge/replicas/duplicateWindow/storageType`; `JetStreamOptions.builder().requestTimeout(Duration)`; `JetStream.publish(String, Headers, byte[])` returns `PublishAck` (`isDuplicate()`); `Headers.put(String, String...)`; `NatsJetStreamConstants.MSG_ID_HDR = "Nats-Msg-Id"`.
- No NATS Testcontainers module on Maven Central (`org.testcontainers:*nats*` not found); tests use `GenericContainer("nats:2.12-alpine")` with `-js`.
- The JPA registry table `event_publication` does not exist (no migration yet; FAPI-4/FAPI-6). Pipeline integration tests get it from the test-only fixture migration `V202609181950__event_publication_stopgap.sql` (`platform.event_publication`, see Pending).

## Out of scope
Writing to the outbox and switching the registry to JDBC (FAPI-6); consumers and inbox; OpenTelemetry propagation.

## TDD
Strict TDD. Runner: `./gradlew test` (JUnit 5, Testcontainers NATS with JetStream + Postgres, `FRAPPE_TEST_DB=frappe_fapi_5`). RED before each behavior.

## Tasks
- [x] T0 Spike (no commit needed): read Modulith 2.1.1 externalization SPI and the Kafka adapter; record the chosen interfaces here
- [x] T1 `DomainEvent` envelope + subject naming (unit tests)
- [x] T2 NATS connection bean and idempotent stream provisioning (integration test: start twice, config matches)
- [x] T3 Externalizer: publish with headers and ack (integration test: exactly one message, `Nats-Msg-Id` = eventId)
- [x] T4 Dedup: same event published twice → stored once
- [x] T5 Failure: NATS unavailable → fails within timeout, publication stays incomplete
- [x] T6 Docs in `writing-code` events reference; `./gradlew check` green (PR left to the orchestrator)

## Acceptance (from ticket)
- One message on the right subject with `Nats-Msg-Id` = eventId.
- Duplicate within window stored once.
- NATS down → failure within timeout, publication incomplete for retry.
- Restart with existing stream succeeds and config matches code.

## Checks
`./gradlew check`.

## Progress / evidence
TDD mode: strict (brief + CLAUDE.md), runner `FRAPPE_TEST_DB=frappe_fapi_5 ./gradlew test`.

| Task | Commit | RED | GREEN |
| --- | --- | --- | --- |
| T1 | 62a6314 | `Uuid7Test` (2) and `NatsSubjectsTest` (3): 5 tests completed, 5 failed (stubs threw `UnsupportedOperationException`) | 5/5 pass |
| T2 | 2aa0258 | `NatsStreamProvisioningTests` (2): 2 failed, `NoSuchBeanDefinitionException` (no `Connection` bean) | 2/2 pass |
| T3 | 1ecd1e7 | `publishesExactlyOneMessageWithEnvelopeHeadersOnceAcked`: `ConditionTimeoutException` (publication never completed, no externalizer) | pass |
| T4 | c555085, 339874b | Behavior already delivered by T3's `Nats-Msg-Id`; test passed on first run. Mutation check: header renamed → `storesAnEventPublishedTwiceWithinTheDuplicateWindowOnce` fails (2 tests, 2 failed); reverted. 339874b re-externalizes through the listener, because two registry rows with equal events were not both marked complete by the JPA registry (flaky in the full suite). | pass |
| T5 | cc458b9 | Behavior already delivered by T3's `requestTimeout(publishTimeout)`; passed on first run. Mutation check: request timeout 30s → `failsWithinTheTimeoutAndKeepsThePublicationIncompleteForRetry` fails with `ConditionTimeoutException` (4s budget); reverted. | pass (also resubmits after unpausing and completes) |
| T6 | 11c20e4 and the docs commit `docs(platform): document externalizing events to NATS…` | `NatsExternalizationRoutingTest` characterization of selection and the missing-envelope message (behavior from T3). | pass |

`FRAPPE_TEST_DB=frappe_fapi_5 ./gradlew check --rerun-tasks`: BUILD SUCCESSFUL twice; 15 tests, 0 failures.

### Review fixes (human decision: the API must start when NATS is down)
- [x] R1 NATS optional at startup. `Nats.connectReconnectOnConnect(Options)` exists in 2.26.2 but blocks the caller in its reconnect loop (with `maxReconnects(-1)`, forever), and `connectAsynchronously` leaves no handle to close a never-connected attempt. Chosen: `NatsClient` (SmartLifecycle) tries `Nats.connect` once at start, logs a WARN and retries on a virtual thread every `reconnect-wait`; after that jnats reconnects forever. A connection listener provisions the stream and resubmits FAILED externalized publications on every CONNECTED/RECONNECTED. Transport fails with "not connected yet" while there is no connection.
- [x] R2 Test isolation: NATS moved to `TestNatsConfiguration`, fresh non-reused container per Spring context; subjects are unique per test and counts are absolute.
- [x] R3 Dedup through the real path: publish in a transaction, read the event back from `CompletedEventPublications`, re-externalize it (plus the direct call), one stored message with the original `Nats-Msg-Id`.
- [x] R4 Shutdown order: `natsEventExternalizer` depends on `natsClient` (asserted), so it is destroyed first; `NatsClient.shutdown` restores the interrupt flag and still closes.
- [x] R5 Provisioner errors carry the server code and description.
- [x] R6 Defaults only in `NatsProperties`; `application.properties` lines removed. `spring-modulith-events-core` kept: without the explicit declaration it is not on `compileClasspath` (`./gradlew dependencies --configuration compileClasspath` shows no `events-core`, compile fails on `org.springframework.modulith.events.support`).
- [x] R7 `@Externalized("value")` on a DomainEvent fails with a message naming the derived subject.

RED (after stubs `NatsClient`/new `provision(Connection)`): 21 tests, 10 failed: `NatsClientTest` x3 (close not invoked; message lacked server description; `UnsupportedOperationException` instead of "not connected"), `rejectsACustomTargetBecauseTheSubjectIsDerived` (no throwable), `startsWithoutNatsAndPublishesPendingEventsOnceItIsUp` (context failed to load: cannot connect), provisioning/externalization tests (no `natsClient` bean). GREEN: 21/21 pass; log shows `WARN NATS is unavailable at nats://localhost:<port>; starting without it...` then `NATS opened`.

### Re-review minors
- [x] M1 close/connect race: only the side that removes the connection atomically (`getAndSet(null)` / `compareAndSet`) drains it.
- [x] M2 Interrupt during the startup attempt: flag restored, WARN, background retry still starts.
- [x] M3 Connect setup (provisioning + resubmission) runs on one single-thread executor, shut down in `close()`.
- [x] M4 `NatsClientTest` cleaned; provisioner test moved to `NatsStreamProvisionerTest`.
RED: `drainsTheConnectionOnceEvenWhenClosedTwice`, `runsConnectSetupOneAtATime`, `keepsRetryingAndTheInterruptWhenStartupIsInterrupted` failed (6 tests, 3 failed). GREEN: 6/6.

### Hardening (PR #10 review)
- [x] H1 UUIDv7 via `com.github.f4b6a3:uuid-creator` 6.1.1 (latest on Maven Central, verified from `maven-metadata.xml`; API from the sources jar: `TimeOrderedEpochFactory.builder().withClock(clock).withIncrementPlus1().build().create()`, lock-protected). Kernel interface `com.frappe.platform.IdGenerator`; `UuidV7IdGenerator` + `Clock`/`IdGenerator` beans (`@ConditionalOnMissingBean`) in `platform.infrastructure.ids`; `Uuid7` removed. RED: `generatesVersion7IdsThatStrictlyIncreaseWithinTheSameMillisecond` failed against a `randomUUID()` stub; GREEN.
- [x] H2 Structured logging. Verified in `spring-boot-4.1.1-sources.jar`: property `logging.structured.format.console` (`LoggingSystemProperty.CONSOLE_STRUCTURED_FORMAT`); logback `ElasticCommonSchemaStructuredLogFormatter` writes MDC entries and SLF4J `getKeyValuePairs()` as JSON fields; an empty format (`hasLength` check in `DefaultLogbackConfiguration`) falls back to the plain pattern. Base config `ecs`, `local` profile empty (human-readable). Context goes through the SLF4J fluent API (`addKeyValue`) with names in `LogFields`. RED: `logsNatsUnavailableAsEcsJsonWithTheUrlAsAField` found no JSON line; GREEN.
  Follow-up: in the full suite Logback is already initialized by cached contexts, so a fresh `SpringApplication` does not switch to ECS. The test now captures our real event with a `ListAppender` and renders it with Boot's `StructuredLogEncoder` using the base config's format; a second test checks base `ecs` and local empty.
- [x] H3 Dedicated exceptions: `EventPublicationException` (cause kept), `NatsProvisioningException` (cause kept), `NatsUnavailableException`, `InvalidExternalizedEventException`, `MissingDatabaseSettingsException` (FAPI-4). No `catch (Exception)`/`catch (RuntimeException)`: the transport catches `IOException | JetStreamApiException | NatsUnavailableException | JacksonException`; the connect setup catches `NatsProvisioningException | DataAccessException`. Log-once decision: the transport turns failures into a failed future (not a rethrow) and is the only place with the event context, so it logs the WARN with `eventId`/`subject`; Modulith additionally logs its own INFO "Leaving event publication uncompleted" (not ours to change). RED: 6 tests failed on exception types / missing class names (`NatsEventTransportTest`, `NatsStreamProvisionerTest`, `NatsClientTest`, 2x routing, `DatabaseConfigurationTests`); GREEN.
- [x] H6 Clean code (human bar): `NatsConfiguration` only wires beans; routing moved to `ExternalizedEventRouter`, connect setup to `NatsConnectSetup`, graceful close to `NatsConnectionCloser`; `NatsClient` keeps connection state and retries (guard clauses, `connectAtStartup`). Magic values named (`UNLIMITED_RECONNECTS`, `MAX_AGE`, `REPLICAS`, `DUPLICATE_WINDOW`). NATS tests use Given/When/Then; drain test moved to `NatsConnectionCloserTest`. Pure refactor: all tests green before and after. FAPI-4 tests left as they are (not this ticket's scope).
- [x] H4 Javadoc gate: `tasks.javadoc` with `memberLevel = PACKAGE`, `-Xdoclint:all`, `-Werror` (no exclusions needed), wired into `check`. RED: `./gradlew javadoc` failed with 26 warnings + "warnings found and -Werror specified" (including FAPI-4's `RequiredDatabaseSettings` default constructor). GREEN after documenting every type/member and adding `package-info.java` per package.
- [x] H5 Skills: new `writing-code` references `ids.md`, `logging.md`, `errors.md`, `documentation.md`, `clean-code.md`, routed from SKILL.md (Hard Rules + Decision Gates); `domain-events.md`/`domain-modeling.md` point to `IdGenerator`; `testing-code` unit/integration references gain Given/When/Then, `TestIds`, per-context NATS and the log-format test approach. Docs only, no behavior.

### Clean-code review changes
- [x] C1 (major) jnats `IllegalStateException` ("Connection is Closed", verified in `NatsConnection`) is wrapped as `NatsUnavailableException` in the new `NatsClient.publish`, so the transport returns a failed future with `EventPublicationException`. RED: `failsThePublicationWhenTheConnectionIsClosing` (mocked closed connection) failed; GREEN.
- [x] C2 (major) URLs are logged and put into messages redacted (`NatsProperties.redactedUrl()`, `scheme://host:port`). RED: `NatsPropertiesTest.redactsCredentialsFromTheLoggableUrl` and the ECS test (now asserting `nats.url` without the password) failed; GREEN.
- [x] C3 Minors: log fields renamed to `frappe.event_id`, `nats.url`, `nats.subject`, `nats.stream`, `nats.connection_event`; constant log patterns; fixed UUID constants instead of `randomUUID` in NATS unit tests; dead `unwrap()` removed; `NatsConfiguration` Javadoc describes beans; why-comment on provisioning twice; `logging.md`, `errors.md`, `ids.md` updated (redaction, field names, Modulith INFO duplicate, no shared base exception, monotonic per instance).

### Pending
- Stopgap until FAPI-6: the app runs as `frappe_app` without DDL (FAPI-4, #9). The registry table now comes from the test-only fixture migration `src/test/resources/db/migration/fixture/V202609181950__event_publication_stopgap.sql` (`platform.event_publication`), and `NatsEventExternalizationTests`, `NatsUnavailableTests` and `NatsStartsWithoutNatsTests` set `spring.jpa.properties.hibernate.default_schema=platform`. FAPI-6 deletes the stopgap migration and the `default_schema` properties, gives its real migration a later version than `V202609181950`, and test databases must be reset (drop the reused `frappe_fapi_*` Postgres containers) because their Flyway history contains the stopgap.

### Rebase on origin/main (FAPI-4, 4a89108)
Kept FAPI-4's `TestcontainersConfiguration`, `application.properties` and `application-local.properties` unchanged (NATS needs no env-specific default: `NatsProperties` defaults to compose's URL). `TestNatsConfiguration` added to `FrappeApiApplicationTests` and `TestFrappeApiApplication`; NATS tests use `@ActiveProfiles("local")`. `FRAPPE_TEST_DB=frappe_fapi_5 ./gradlew check`: BUILD SUCCESSFUL.

## Next step
Open the PR per template; FAPI-6 replaces the stopgap fixture migration with the real `event_publication` migration (see Pending).
