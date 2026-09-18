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
- [ ] T3 OTLP endpoint down → API keeps serving; test
- [ ] T4 NATS publish observation in the FAPI-5 transport; test
- [ ] T5 Business metrics facade from domain events + tag policy guard (no tenant/entity tags); tests
- [ ] T6 `otel-lgtm` in compose + README (how to open Grafana); manual check documented
- [ ] T7 Skill `observing-the-api` + vendored skills + AGENTS.md routing; Ticket Standard note for Plane (orchestrator updates the page)
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

## Next step
T0.
