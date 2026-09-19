# Observability

Infrastructure telemetry is automatic; feature code declares business metrics only. Conventions, SLOs and alerting: skill `observing-the-api`.

## Automatic (never hand-write)

- HTTP server/client, JDBC (`datasource-micrometer`), connection pool, JVM, Spring Modulith module entries and cross-module listeners, NATS publishes (`nats.publish`, PRODUCER) and consumes (`nats.process`, CONSUMER, via `NatsProcessObservations`), outbox recovery (`outbox.recovery`, `outbox.redelivery`, gauge `outbox.dead.letters`), every use case call (`@CommandUseCase` / `@QueryUseCase`; `use_case`: span `<module> <UseCase>`, timer tagged `use_case.name`, `use_case.module`, `use_case.kind`, `outcome` = `success` | `failure` | `error`; see `use-cases.md`), scheduled task executions (`scheduled.task`, tagged `scheduled.task.name` and `scheduled.task.outcome`; counter `scheduled.task.exhausted` for given-up one-time runs): spans and metrics come from the platform.
- W3C trace context crosses the outbox and NATS automatically (`traceparent` / `tracestate` headers): publish and process spans link to the trace the event was recorded in (`writing-code/references/domain-events.md`, "Trace context"). Never parent a span on a received message context.
- Logs carry `trace.id`/`span.id` inside a trace and are exported over OTLP as well (`OtlpLogBridge`, Logback → Boot's `SdkLoggerProvider`); the console output is unchanged.
- Micrometer Observation is the only telemetry facade, and only in `infrastructure`. Domain and application never import Micrometer, OpenTelemetry or tracing types. No OTel Java agent, no `@Observed`/`@Timed` sprinkled on handlers.

## Business metrics (the one thing features add)

Annotate the domain event record (kernel annotations, plain Java):

```java
@Counted(name = "tabs.closed", description = "Tabs closed", tags = @MetricTag(key = "channel", from = "channel"))
@Measured(name = "tabs.revenue", description = "Revenue of closed tabs", value = "total", unit = MetricUnit.MONEY)
public record TabClosed(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion,
        int eventVersion, UUID tenantId, Channel channel, Money total) implements DomainEvent {}
```

- Recorded after the publishing transaction commits (rolled back: never counted), as `frappe.<module>.<name>`.
- Richer cases: a `BusinessMetricsDeclaration` bean in the module's `infrastructure` using the fluent `BusinessMetrics` API (`metrics.on(TabClosed.class).count("tabs.split", "…").tag("channel", TabClosed::channel)`).
- Invalid declarations stop startup with every problem listed.

## Tag policy

- Metric tags are low cardinality only: enum or boolean fields (`channel`, `outcome`, `split`), plus `currency` added for money. The API enforces it by type.
- Tenant, branch, user, aggregate and entity ids are never metric tags: they go on spans (high-cardinality key values) and logs.
- Names: lowercase dotted words, no `frappe.` prefix (added), no `total` suffix, base units (`seconds`, `bytes`, `minor_units`), description required.

## Privacy in spans

JDBC spans carry the SQL text; `jdbc.datasource-proxy.include-parameter-values=false` keeps bound values out. SQL therefore always uses bind parameters, never literals with personal data. Span attributes hold ids and bounded values only, never names, emails, tokens or payloads.

## Failure

Telemetry never fails a business operation: exporters run on their own threads and drop on failure; recording a business metric is an isolation boundary (see `errors.md`): any failure of one metric (throwing declaration, missing, negative or non-finite value, registry conflict) is skipped with one WARN (`frappe.metric`, `frappe.event_type`, `frappe.event_id`). Meter types are checked against the registry at startup, and each series is registered once and reused.
