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
- [ ] T2 Producer span + header injection on publish, using the stored context (also after resubmission)
- [ ] T3 Consumer helper: extract and link; no header → works without link; malformed → works, WARN once
- [ ] T4 Docs (`writing-code` events, `observing-the-api`), `./gradlew check` green, PR per template

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
- Links: the Observation API has no link concept; only `io.micrometer.tracing.Span.Builder#addLink(Link)` (`Link(TraceContext)`), which the OTel bridge (`OtelSpanBuilder#addLink`) maps to an OTel span link. `OtelSpanBuilder` without `setParent` starts a **root** span (it calls `setNoParent`), so a custom handler must set the parent explicitly.
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

## Next step
T2.
