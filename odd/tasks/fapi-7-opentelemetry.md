# FAPI-7 — platform: Observe requests, persistence and module events with OpenTelemetry

Plane: [FAPI-7](https://app.plane.so/nulled-software/browse/FAPI-7/) (module platform, size M; read the ticket comments: scope additions). Branch: `feat/fapi-7-opentelemetry`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-7`.

## Objective
Observability from day one with no telemetry code in features: infrastructure metrics and traces are automatic; developers only declare business metrics.

## Decisions
- `org.springframework.boot:spring-boot-starter-opentelemetry` 4.1.1 and `org.springframework.modulith:spring-modulith-observability` 2.1.1 (verified on Maven Central). Micrometer Observation is the only facade code may use; no OTel Java agent.
- OTLP export for traces and metrics; endpoint and sampling configurable; `local` samples 100%.
- Automatic spans/metrics: HTTP server/client, JDBC/Hibernate, connection pool, JVM, Modulith module calls and listeners, NATS publish (instrument the FAPI-5 transport once, in infrastructure).
- `traceId`/`spanId` as fields in ECS JSON logs.
- Local: `grafana/otel-lgtm` in compose, zero config for the app.
- Metric policy: only low-cardinality tags (module, operation, outcome); tenant/entity ids only as span attributes.
- Business metrics: declared from domain events in the infrastructure layer through one small facade (mapping event → counter/distribution with allowed tags); domain stays pure. Build the facade with one example driven by a test event.
- Own skill `observing-the-api` (SRE conventions: golden signals, RED/USE, OTel semantic conventions and Micrometer naming, tag policy, SLO buckets, SLIs/SLOs, burn-rate alerting, how to declare a business metric). Every feature ticket lists business metrics under Contracts; reviewers reject missing ones and hand-written technical metrics.
- Vendor into `.agents/skills` (record in `THIRD_PARTY.md`, route from `AGENTS.md`, check conflicts with our standards): `grafana/skills@opentelemetry`, `grafana/skills@prometheus-label-strategy`, `grafana/skills@promql`, `wshobson/agents@slo-implementation`, `jabrena/plinth@182-java-observability-metrics-micrometer`, `jabrena/plinth@183-java-observability-tracing-opentelemetry`, `jabrena/plinth@126-java-exception-handling`, `jabrena/plinth@181-java-observability-logging`.
- Standards from `writing-code` apply (Javadoc, errors, logging, clean code).

## Out of scope
Trace propagation through outbox/NATS (FAPI-8); command/query bus observation (bus ticket); production collector (deploy ticket).

## TDD
Strict TDD. Runner: `./gradlew test` (in-memory exporters / `TestObservationRegistry`, Testcontainers, `FRAPPE_TEST_DB=frappe_fapi_7`). RED before each behavior.

## Tasks
- [x] T0 Verify Boot 4.1.1 starter auto-config (property names, what it instruments, JDBC instrumentation choice) from sources; record here
- [x] T1 Starter + Modulith observability + OTLP config per profile; test: request produces HTTP and DB spans (in-memory exporter)
- [x] T2 Trace/span ids in ECS logs; test
- [x] T3 OTLP endpoint down → API keeps serving; test
- [x] T4 NATS publish observation in the FAPI-5 transport; test
- [x] T5 Business metrics facade from domain events + tag policy guard (no tenant/entity tags); tests
- [x] T6 `otel-lgtm` in compose + README (how to open Grafana); manual check documented
- [x] T7 Skill `observing-the-api` + vendored skills + AGENTS.md routing; Ticket Standard note for Plane (orchestrator updates the page)
- [ ] T8 `./gradlew check` green; PR per template

## Acceptance (from ticket and comments)
- Request trace with HTTP + DB spans visible in Tempo locally.
- Log lines carry trace and span ids.
- Module listener span in the same trace.
- OTLP down → API unaffected.
- No metric with tenant/entity id tags.
- A business metric is declared from a domain event without touching the domain.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_7 ./gradlew check`; manual Grafana check.

## Progress / evidence

### T0 findings (verified from the 4.1.1 / 2.1.1 jars in the Gradle cache: auto-configuration imports, configuration metadata, bytecode; Context7 was not available in this session)
- `spring-boot-starter-opentelemetry` 4.1.1 pulls `spring-boot-opentelemetry` (OTel SDK, OTLP log export), `spring-boot-micrometer-tracing-opentelemetry` (Micrometer Tracing bridge-otel, OTLP span exporter) and `spring-boot-starter-micrometer-metrics` (+ `micrometer-registry-otlp`). Test starter: `spring-boot-starter-opentelemetry-test` (`@AutoConfigureTracing`, `micrometer-observation-test`).
- Properties (4.1.1 names): `management.tracing.sampling.probability` (default `0.1`), `management.tracing.enabled`, `management.tracing.export.enabled`; spans: `management.opentelemetry.tracing.export.otlp.endpoint` (no default; the span exporter bean exists only when it is set or a connection-details bean exists), `.transport` (`http`), `.timeout`, `management.opentelemetry.tracing.export.schedule-delay` (5s); metrics: `management.otlp.metrics.export.url` (Micrometer default `http://localhost:4318/v1/metrics`), `.enabled` (true), `.step` (1m); logs: `management.opentelemetry.logging.export.otlp.endpoint`; resource: `management.opentelemetry.resource-attributes`. `management.otlp.tracing.*` are deprecated aliases.
- Docker Compose: `OpenTelemetryTracingDockerComposeConnectionDetailsFactory`, `OpenTelemetryMetricsDockerComposeConnectionDetailsFactory`, `OtlpLoggingDockerComposeConnectionDetailsFactory` recognise image `grafana/otel-lgtm` (and `otel/opentelemetry-collector-contrib`): zero config for `bootRun` through `spring-boot-docker-compose`.
- JDBC: Boot 4.1.1 has no JDBC observation (only pool metrics `DataSourcePoolMetricsAutoConfiguration` and `HibernateMetricsAutoConfiguration`). Choice: `net.ttddyy.observation:datasource-micrometer-spring-boot` 2.3.0, built against Boot 4.1.1 (pom verified on Maven Central); it wraps the `DataSource` and emits connection/query spans; Hibernate SQL goes through JDBC so it is covered.
- Log correlation: Micrometer Tracing puts `traceId`/`spanId` in the MDC; Boot's ECS formatter writes MDC entries as JSON fields; `logging.structured.json.rename` can map them to ECS `trace.id`/`span.id`.
- Spring Modulith 2.1.1 observability (`spring-modulith-observability-core`, already on the classpath since FAPI-5): `ModuleObservabilityAutoConfiguration` is active only with `management.tracing.enabled=true`; wraps module API beans and event listeners in observations (low keys `module.identifier`, `module.name`, `module.invocation-type`, `module.method`; high key `module.event.type`) and counts published events per module (`module.events.published`).
- In `@SpringBootTest`, tracing and metrics export are off unless the test opts in with `@AutoConfigureTracing` / `@AutoConfigureMetrics`; tests then use an in-memory span exporter (`opentelemetry-sdk-testing`, managed by the OTel BOM).

### T1
- RED `RequestTracingTests.aRequestProducesAnHttpServerSpanWithDatabaseSpansInTheSameTrace`: first run (sampling 0.1, no JDBC instrumentation) timed out waiting for the server span; with local sampling 1.0 but without `datasource-micrometer-spring-boot` it failed with `Expecting ArrayList ["http get /test/observability-probe"] to contain ["query"]`.
- GREEN after adding `datasource-micrometer-spring-boot` 2.3.0: server span `http get /test/observability-probe` plus `connection`, `query` (`jdbc.query[0]=select 1`) and `result-set` spans in the same trace.
- `observesApplicationModuleEntriesAndListeners`: guard test (no RED, the processor already came with FAPI-5's runtime dependency). Spring Modulith observes only controllers, exposed module API types and listeners to other modules' events, and `ApplicationModules` excludes test classes, so a test-only listener cannot prove a listener span; the end-to-end listener span is proven with the first business module (open question).

### T2
- RED `TraceCorrelationLoggingTests.writesTheTraceAndSpanIdsAsEcsFields`: `expected "4bf9…4736" but was ""` (MDC ids rendered as `traceId`/`spanId`).
- GREEN with `logging.structured.json.rename[traceId]=trace.id` / `[spanId]=span.id`. Boot writes renamed members as flat dotted keys (`"trace.id":…`), the same shape the official ecs-logging libraries emit; ECS accepts dotted and nested forms.
- `RequestTracingTests.logLinesOfARequestCarryItsTraceAndSpanIds`: characterization (Boot's default log correlation), first run failed only because the in-memory exporter kept spans of the previous test; fixed with `@BeforeEach spans.reset()`, then GREEN.

### T3
- `OtlpUnavailableTests.keepsServingWhileTheOtlpEndpointIsUnreachable`: both exporters pointed at `localhost:1`, export rounds every 50–100 ms; all requests `200`, the registry only logs `WARN Failed to publish metrics to OTLP receiver`. No RED possible: this is a characterization test of the SDK design (batch span processor and meter registry export on their own threads, drop on failure); it guards against a future synchronous exporter or a failing customizer.

### T4
- RED `NatsEventTransportTest.observesASuccessfulPublishWithTheSubjectAsALowCardinalityKey` / `observesAFailedPublishWithItsError`: compilation failed, `NatsEventTransport` had no `ObservationRegistry` (no observation existed).
- GREEN: `NatsPublishObservation` (name `nats.publish`, contextual name `publish <subject>`, low keys `messaging.system=nats`, `messaging.destination.name=<subject>`, high key `messaging.message.id=<eventId>`), started around the publish in `NatsEventTransport`, error recorded on failure, stopped in `finally`. Shared-file footprint: one constructor parameter in `NatsEventTransport` and one bean parameter in `NatsConfiguration` (FAPI-6 overlap kept minimal). Span kind stays INTERNAL: a PRODUCER span needs a `SenderContext` that injects headers, which is trace propagation (FAPI-8).

### T5 (business metrics, extended by the human's mid-ticket requirement)
Design as requested, with justified deviations:
- Primary path: kernel annotations `@Counted`, `@Measured` (repeatable), `@MetricTag`, `MetricUnit` in `com.frappe.platform` (plain Java). `BusinessMetricsConfiguration` scans the application packages once at startup (`AnnotatedEventScanner`), validates (`MetricRules`), and `BusinessMetricsRecorder` records after commit.
- Escape hatch: kernel `BusinessMetrics` fluent API + `BusinessMetricsDeclaration` bean (`metrics.on(TabClosed.class).count("tabs.split", "…").tag("channel", TabClosed::channel).flag("split", TabClosed::split)`), no Micrometer types exposed.
- Standardized: `frappe.<module>.<name>` from the event's package, lowercase dotted names, no `frappe.` prefix, no `total` suffix, description required, base units (`items`, `seconds`, `bytes`, `minor_units`), money as minor units + `currency` tag, histogram buckets for `SECONDS` (SLO buckets per metric via `management.metrics.distribution.slo.<name>`).
- Guardrails fail at startup (`InvalidBusinessMetricException` listing every problem) and in tests (`BusinessMetricAssert.assertValidBusinessMetrics`).
- Test DX: `assertThatBusinessMetric(registry, name).withTag(k, v).hasCount(n).hasTotal(x)`.
- Deviation 1, tags: allowlist by type (enum or boolean fields only; typed `tag(key, Function<E, Enum>)`/`flag(key, Predicate<E>)` in the fluent API) instead of a central list of keys: bounded by construction, zero maintenance. The human's example `@Tag(key = "branch", from = "branchId")` is rejected on purpose: branch ids are UUIDs, unbounded across tenants; per-branch views come from traces/logs, or later from a bounded dimension.
- Deviation 2, names: `@MetricTag` instead of `@Tag` (clashes with JUnit's `@Tag` and Micrometer's `Tag`); unit `MONEY` instead of `MONEY_MINOR` (the exported base unit is `minor_units`).
- Deviation 3, assertion order: `withTag(...)` narrows before `hasCount(...)` (filter then assert) instead of `hasCount(1).withTag(...)`, so a failure names the exact series.
- Money: there is no `Money` type yet; money is recognized structurally (record with `long minorUnits` and `Currency currency`, the documented shape). The fluent API rejects `MONEY` (needs the currency) and points to `@Measured`.
- Recording: plain `@EventListener` + `TransactionSynchronization.afterCommit`, because Spring Modulith stores an outbox publication for every `@TransactionalEventListener` (verified in `PersistentApplicationEventMulticaster` 2.1.1). Read failures (null field) are skipped with one WARN; the publisher never sees an exception.
- Invalid-declaration fixtures live in `src/test/java/fixtures/invalidmetrics`, outside the `com.frappe` scan root, otherwise every Spring test context would (correctly) refuse to start.
- RED: compilation failed (`Counted`, `Measured`, `MetricTag`, `MetricUnit`, `BusinessMetricDefinitions`, `BusinessMetricsRecorder`, `DeclaredBusinessMetrics`, `InvalidBusinessMetricException` missing). First GREEN run: unit tests green; `BusinessMetricsIntegrationTests` failed twice for test reasons: `expected 1L but was 2L` (cached context shared across tests; fixed with a dedicated `DELIVERY` channel) and the tag guard flagged the JVM memory-pool tag `id` (bounded pool name; guard narrowed to tenant and `<entity>_id`/`<entity>Id` keys). Then GREEN: `BusinessMetricDefinitionsTest` (7), `BusinessMetricsRecorderTest` (4), `DeclaredBusinessMetricsTest` (2), `BusinessMetricsIntegrationTests` (2: committed-only counting, no tenant/entity tag on any meter).

### T6
- RED `LocalObservabilityStackTest.composeRunsGrafanaOtelLgtmOnTheStandardPorts`: no `otel-lgtm` service. GREEN after adding `grafana/otel-lgtm:0.33.1` (3000, 4317, 4318) to `compose.yaml`.
- Manual check (isolated compose project `frappe-fapi7-otelcheck`, only `otel-lgtm`; app via `bootRun` on port 18087 against this ticket's Testcontainers Postgres, OTLP endpoints set explicitly since 5432 is taken by another project): Tempo search `service.name=frappe-api` returned `http get /actuator/health` traces containing `connection` JDBC spans; Prometheus listed `http_server_requests_*`, `hikaricp_connections_active`, `jvm_*`. Torn down with `down -v`.
- Finding: Loki stays empty; OTLP log export needs the OpenTelemetry Logback appender, not wired (out of this ticket's acceptance; logs are ECS JSON on stdout with `trace.id`). README says so. Follow-up candidate.
- Incident: the first manual `bootRun` pointed `FRAPPE_NATS_URL` at a NATS container of another test run for a few seconds; the provisioner reported the `FRAPPE` stream "up to date" (no change) and the app was restarted with NATS unreachable.

### T7
- Own skill `.agents/skills/observing-the-api` (`SKILL.md` with the 10-line primary example, `references/conventions.md`, `references/slos.md`); `writing-code/references/observability.md` (tag policy), `testing-code/references/observability-tests.md` (`TestObservationRegistry`, in-memory exporter, business metric assertions); routing in `AGENTS.md`.
- Vendored unmodified from clones (upstream `LICENSE` copied into each directory; recorded in `THIRD_PARTY.md`): `grafana/skills@05196628` (`opentelemetry`, `promql`, `prometheus-label-strategy`), `wshobson/agents@4236bb91` (`slo-implementation`, MIT), `jabrena/plinth@9a3dc292` (`126-java-exception-handling`, `181-java-observability-logging`, `182-java-observability-metrics-micrometer`, `183-java-observability-tracing-opentelemetry`).
- Conflicts found (ours win, listed in `observing-the-api`): Java agent and `OTEL_*` config vs Boot starter and `management.*`; `tenant_id` tolerated as a label vs never; `http_requests_total` example names vs Micrometer's `http_server_requests_seconds_*`; hand-built meters in services vs event declarations; OTel API manual spans vs Micrometer Observation in adapters; `logback.xml` and manual MDC vs Boot ECS properties; exceptions for validation and Maven vs `Result` and Gradle.

### Ticket Standard addition (draft for the orchestrator to put in Plane)

Under **Contracts**, add:

> **Business metrics.** List every business metric the feature records, or write "none" with the reason. One line per metric: full name (`frappe.<module>.<noun>.<past-participle>`), type (counter / distribution), unit for distributions (`items`, `seconds`, `bytes`, `money`), tags (enum or boolean event fields only; never tenant, branch, user or entity ids) and the domain event it is declared on (`@Counted` / `@Measured`, or a `BusinessMetricsDeclaration` when the annotations cannot express it). Example: `frappe.order.tabs.closed` — counter — tags `channel` — on `TabClosed`. Acceptance includes a test per metric (`assertThatBusinessMetric`). Infrastructure telemetry (HTTP, database, messaging, JVM) is automatic and is never listed or hand-written; reviewers reject missing business metrics and hand-written technical metrics or spans.

## Next step
T0.
