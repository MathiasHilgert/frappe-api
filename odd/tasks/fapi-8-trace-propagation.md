# FAPI-8 — platform: Propagate trace context through the outbox and NATS

Plane: [FAPI-8](https://app.plane.so/nulled-software/browse/FAPI-8/) (module platform, size M, sensitive: events). Branch: `feat/fapi-8-trace-propagation`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-8`.

## Objective
The trace that caused an event survives the asynchronous outbox and NATS hop: producers carry W3C trace context, consumers link to it.

## Decisions
- Capture W3C `traceparent` / `tracestate` when the event is recorded (inside the handler transaction, where the trace is active) and store it with the publication, so resubmissions after outages keep the original context. Choose the storage after reading how FAPI-6 records publications (e.g. a side table in `platform` keyed by publication/event id, or envelope metadata) — never change the domain event contract for telemetry.
- Inject into NATS headers at publish with a PRODUCER span (turn FAPI-7's `nats.publish` observation into a `SenderContext`-based producer observation).
- Consumer helper: extract headers and start the consumer span with a span LINK to the producer (not a parent). Ready for the first consumer and inbox; tested with a test consumer.
- Micrometer Observation / Propagator APIs only; no raw OTel in feature code.
- Missing or malformed headers never fail publishing or consuming.
- Standards from `writing-code` and `observing-the-api` apply.

## Out of scope
Inbox and concrete consumers.

## TDD
Strict TDD. Runner: `./gradlew test` (in-memory span exporter, Testcontainers Postgres + per-context NATS, `FRAPPE_TEST_DB=frappe_fapi_8`). RED before each behavior.

## Tasks
- [x] T0 Verify Micrometer Tracing / Boot 4.1.1 propagation APIs (Propagator, SenderContext/ReceiverContext, links) from sources; decide trace-context storage; record here
- [x] T1 Capture trace context at record time and persist it with the publication (commit/rollback semantics match the outbox)
- [x] T2 Producer span + header injection on publish, using the stored context (also after resubmission)
- [x] T3 Consumer helper: extract and link; no header → works without link; malformed → works, WARN once
- [x] T4 Docs (`writing-code` events, `observing-the-api`), `./gradlew check` green, PR per template

## Acceptance (from ticket)
- A command that publishes an event → NATS message carries the originating `traceparent`.
- Resubmitted after a NATS outage → still the original context.
- Consumer span links to the producing span.
- Event recorded without an active trace → publishing works, no header.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_8 ./gradlew check`.

## Progress / evidence

### T0 findings (verified from the jars in the Gradle cache: micrometer-tracing 1.7.1 sources, micrometer-observation 1.17.1 sources, spring-modulith-events-core 2.1.1 sources, bytecode of micrometer-tracing-bridge-otel 1.7.1 and spring-boot-micrometer-tracing / -observation 4.1.1)
- Resolved versions: Boot 4.1.1, Micrometer Tracing 1.7.1 (OTel bridge), Micrometer Observation 1.17.1, OpenTelemetry 1.62.0, Spring Modulith 2.1.1.
- `io.micrometer.tracing.propagation.Propagator#inject(TraceContext, C, Setter<C>)` writes the configured propagation fields; with Boot's default W3C propagation that is `traceparent`, `tracestate` (only when non-empty) and `baggage` (only when baggage exists). `Propagator#extract` returns a `Span.Builder` with the extracted context as **parent**; the extracted `TraceContext` itself is not exposed.
- `PropagatingSenderTracingObservationHandler` (Boot bean, `@Order(2000)`) creates a span of `SenderContext#getKind()` and **injects that new span** into the carrier. `PropagatingReceiverTracingObservationHandler` (`@Order(1000)`) makes the extracted context the **parent** of the receiver span. `DefaultTracingObservationHandler` (`@Order(Ordered.LOWEST_PRECEDENCE - 1000)`) creates INTERNAL children. Boot groups every `TracingObservationHandler` bean (`ObjectProvider.orderedStream()`) into one first-matching composite, so a handler bean with a lower order that supports only our own context type wins for that type and leaves every other observation untouched.
- Links: the Observation API has no link concept; only `io.micrometer.tracing.Span.Builder#addLink(Link)` (`Link(TraceContext)`), which the OTel bridge (`OtelSpanBuilder#addLink`) maps to an OTel span link. `OtelSpanBuilder` without `setParent` falls back to OTel's implicit current context (it calls `setNoParent` only when asked; corrected after review, the first reading of the bytecode was wrong), so the handler sets the parent explicitly to stay independent of bridge behaviour.
- `Tracer#traceContextBuilder()` builds a remote `TraceContext` from trace id, span id and sampled flag (OTel bridge: `SpanContext.createFromRemoteParent`); that is how a link target is built from a received `traceparent` with Micrometer API only.
- Spring Modulith 2.1.1 has no metadata on event publications (`TargetEventPublication` and the JDBC repository columns are fixed; the `EventSerializer` must return the event type). The NATS externalizer (`EventExternalizerModuleListener`) is an `@ApplicationModuleListener` (async, after commit), so the publish never runs in the recording thread, and resubmissions run in the recovery trace.

### Decisions (T0)
- **Storage**: side table `platform.event_trace_context (event_id uuid pk, traceparent, tracestate, recorded_at)` (new Flyway migration in `db/migration/platform`), written in the recording transaction by the outbox adapter, only for events selected for externalization and only when a trace is active. Commit and rollback therefore match the outbox rows. The domain event contract and Modulith's tables stay untouched. The insert uses `on conflict do nothing` and pre-validated values, so it can only fail when the database itself fails, which fails the outbox insert of the same transaction anyway. Retention: rows are purged together with the outbox archive (existing follow-up), hence `recorded_at`.
- **Creation context (OpenTelemetry messaging semantic conventions)**: the context active when the event is recorded is the message's *creation context*. The NATS message carries exactly that context (`traceparent`, `tracestate`), on the first publish and on every resubmission; no header when none was recorded.
- **Producer span**: `nats.publish` becomes a PRODUCER span that is a child of whatever is current when publishing (the listener, or the recovery pass on resubmission) and **links** to the creation context. It is deliberately not a `SenderContext`: Boot's sender handler would inject the publish span itself into the headers (a header even without a recorded trace, and a resubmission would carry the recovery trace instead of the original one). A small `LinkedMessageContext` + `LinkedMessageTracingHandler` (Micrometer Tracing API, ordered before Boot's handlers) creates PRODUCER/CONSUMER spans with the link.
- **Consumer**: `NatsProcessObservations` creates a CONSUMER span `process <subject>` that links to the creation context from the headers (never a parent). No header: no link, no log. Malformed header: no link; the first one per process is logged at WARN, later ones at DEBUG (no log flood from one broken producer).
- Telemetry never fails publishing: a failing lookup of the stored context (`DataAccessException`) publishes without trace headers and logs one WARN.

### T1 capture and persist
- RED `W3cTraceContextTest` (17 cases): compilation failed, `W3cTraceContext` missing. GREEN: record with `parse` (never throws; malformed traceparent → empty, malformed tracestate dropped, W3C 512-char limit) and the validating canonical constructor.
- RED `TraceContextRecordingIntegrationTests`: first `BadSqlGrammarException` (no table); with migration `V202609190900__create_event_trace_context.sql`, `recordsTheActiveTraceContextWithAnExternalizedEvent` failed with `Expecting Optional to contain a value but was empty` (nothing recorded yet). First GREEN attempt failed on the test itself: OTel 1.62 writes flags `03` (sampled + W3C level 2 random trace id), not `01`; the assertion now parses the value and checks trace id and the sampled bit. GREEN: 4/4 (records in the command trace; nothing without a trace; rolled back with the event; nothing for an event that is not externalized). `DomainEventPublisherIntegrationTests` 5/5 still green.
- Code: `tracing.EventTraceContexts#recordCurrent` (Tracer + configured `Propagator`), `EventTraceContextRepository` (`on conflict do nothing`), `OutboxDomainEventPublisher` records the context for events selected by `EventExternalizationConfiguration`.
- `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T2 producer span and headers
- RED (compilation): `NatsEventTransportTest` (4 new: headers carried, empty tracestate omitted, no headers without a recorded context, PRODUCER `LinkedMessageContext` with the creation context) and `EventTraceContextsTest` (2: lookup, failing lookup → empty + one WARN with `frappe.event_id` and cause): `LinkedMessageContext`, `EventTraceContexts#recordedFor`, `EventTraceContextRepository#find` and the new transport constructor missing. GREEN: 8/8 and 2/2.
- RED `NatsTracePropagationTests.observesThePublishAsAProducerSpanLinkedToTheRecordedContext` with everything but the handler bean: `expected: PRODUCER but was: INTERNAL` (Boot's default handler took the new context). The acceptance tests `carriesTheTraceContextTheEventWasRecordedIn` (A1), `keepsTheOriginalTraceContextWhenResubmittedAfterAnOutage` (A2, NATS paused, resubmitted in another trace) and `publishesWithoutTraceHeadersWhenTheEventWasRecordedWithoutATrace` (A4) were written in the same RED step and passed once the transport wrote the headers. GREEN after `TracingConfiguration` registered `LinkedMessageTracingHandler` at order 0: 4/4.
- Added to the A2 test afterwards (green at once, a guard of the design, no RED): the successful publish after the resubmission is in another trace than the command and links to it, so late delivery never stretches the producing trace.
- `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T3 consumer helper
- RED (compilation) `NatsProcessObservationsTest` (3): `NatsProcessObservations` missing. GREEN 3/3: CONSUMER `LinkedMessageContext` linked to the creation context, name `nats.process`, contextual name `process <subject>`, `messaging.*` keys; a message without headers or without `traceparent` is processed without a link and without a log line; a malformed `traceparent` is ignored, the first occurrence logged at WARN (`nats.subject`, `frappe.event_id`), the second at DEBUG.
- `NatsTracePropagationTests.aConsumerSpanLinksToTheProducingSpanInsteadOfJoiningItsTrace` (A3): a test consumer subscribes to the subject, processes the message through the helper; the CONSUMER span is in another trace and links to the command trace. Green on the first run (helper and span handler already existed from the unit RED/GREEN above, so this acceptance test is a guard, not its own RED). 5/5 in the class.
- Refactor: the `messaging.*` key names moved to `MessagingObservationKeys`, shared by publish and process observations.
- `./gradlew spotlessApply check`: BUILD SUCCESSFUL.

### T4 docs and verification
- `writing-code/references/domain-events.md`: header list extended and a new "Trace context" section (creation context, storage, headers on every publish, PRODUCER/CONSUMER spans that link, failure behaviour, purge with the archive); "Consuming" points at `NatsProcessObservations`.
- `writing-code/references/observability.md`: `nats.process` added to the automatic telemetry and a line on links instead of parents.
- `observing-the-api/references/conventions.md` (automatic spans and the link rule) and `observing-the-api/SKILL.md` (the FAPI-8 placeholder now describes the shipped behaviour); README's Tempo walkthrough mentions the propagated context.
- Verification `FRAPPE_TEST_DB=frappe_fapi_8 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 44 test classes, 160 tests, 0 failures.

### Review minors (both reviews approved, no blockers or majors)
- R1 parent fallback: `LinkedMessageTracingHandler` falls back to `tracer.currentSpan()` when the parent observation has no span. Test `aSpanMadeCurrentOutsideAnObservationStillParentsTheConsumerSpan` passed before the change: the OTel bridge already parents implicitly on its current context (T0 note corrected). No RED possible with this bridge; the explicit fallback removes the dependency on bridge behaviour and the test guards it.
- R2 stricter acceptance tests: A3 asserts the single link targets the creation context's span id; A2 asserts the resubmitted publish span belongs to the recovery trace (it does: Modulith's async listener runs in the resubmitting trace). Both green on first run (assertion tightening, no behaviour change); 6/6 in `NatsTracePropagationTests`.

## Follow-ups / open questions
- Purging `platform.event_trace_context` belongs to the existing follow-up that purges `platform.event_publication_archive`; until then the table grows with the outbox history.
- "WARN once" for a malformed `traceparent` is per `NatsProcessObservations` instance (one bean, so once per process); later occurrences are DEBUG. If a per-producer counter is wanted, that is a follow-up.
- The consumer helper has no production caller yet (inbox and concrete consumers are out of scope); it is proven by a test consumer against real NATS.
- The first publish attempt runs in the trace of the command (Modulith's async listener), which is intended: only late deliveries are linked instead of parented.

## Next step
Review and PR (not created here: no push, no Plane change).
