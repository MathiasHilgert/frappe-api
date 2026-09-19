---
name: observing-the-api
description: "Frappé observability conventions: business metrics on domain events (@Counted/@Measured), automatic infrastructure telemetry, naming and tag policy, golden signals, SLIs/SLOs and burn-rate alerts. Use when a ticket needs metrics, spans, dashboards, SLOs or alerts, or when reviewing telemetry."
license: Proprietary
metadata:
  author: "MathiasHilgert"
  version: "1.0"
---

## Activation Contract

Load when a ticket lists business metrics under Contracts, when touching telemetry, dashboards, SLOs or alerts, and when reviewing any feature (every feature ticket declares its business metrics). Code-level rules live in `writing-code` (`references/observability.md`); tests in `testing-code` (`references/observability-tests.md`).

## The primary path: declare a business metric

```java
@Counted(name = "tabs.closed", description = "Tabs closed",
        tags = @MetricTag(key = "channel", from = "channel"))
@Measured(name = "tabs.revenue", description = "Revenue of closed tabs",
        value = "total", unit = MetricUnit.MONEY)
public record TabClosed(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion,
        int eventVersion, UUID tenantId, Channel channel, Money total) implements DomainEvent {}
```

That is all: exported as `frappe.order.tabs.closed` (Prometheus `frappe_order_tabs_closed_total{channel="dine_in"}`) and `frappe.order.tabs.revenue` (minor units, `currency` tag), recorded after the transaction commits. Test it with `assertValidBusinessMetrics(TabClosed.class)` and `assertThatBusinessMetric(registry, "frappe.order.tabs.closed").withTag("channel", "dine_in").hasCount(1)`. Anything the annotations cannot express: a `BusinessMetricsDeclaration` bean using the fluent `BusinessMetrics` API in the module's `infrastructure`.

## Hard Rules

- Developers write business metrics only, and only as event declarations. Infrastructure telemetry (HTTP, JDBC, pool, JVM, Modulith, NATS, scheduled tasks) is automatic; hand-written technical meters or spans in features are rejected in review.
- No telemetry types (Micrometer, OpenTelemetry) in `domain` or `application`; Micrometer Observation is the only facade, in `infrastructure`. No OpenTelemetry Java agent.
- Metric tags are low cardinality: enum or boolean fields (`channel`, `outcome`, `split`), `currency` for money. Tenant, branch, user, aggregate and entity ids go on spans and logs, never on metrics.
- Names: `frappe.<module>.<noun>.<past-participle>` (`tabs.closed`, `payments.refunded`), lowercase dotted, no `total` suffix, base units; description says what one recording means.
- Every feature ticket lists its business metrics under Contracts (or states "none" with the reason); reviewers reject missing ones.
- Telemetry never fails or slows a business operation; the API starts and serves without an OTLP receiver.

## Decision Gates

| Question | Reference |
| --- | --- |
| Which metric does this feature need? Counter or distribution? Names, units, tags | `references/conventions.md` |
| SLIs, SLOs, error budgets, burn-rate alerts, dashboards | `references/slos.md` |
| PromQL for a panel or alert | vendored `promql` (translate names per `references/conventions.md`) |
| Label/cardinality audit | vendored `prometheus-label-strategy` (our tag policy is stricter) |
| SLO frameworks and templates | vendored `slo-implementation` |
| OTLP, collector, sampling | vendored `opentelemetry` (we use the Spring Boot starter, not the agent) |
| Generic Micrometer, tracing, logging, exception guidance | vendored `182-…`, `183-…`, `181-…`, `126-…` (ours win on conflict) |

## Conflicts with vendored skills (ours win)

- `opentelemetry` recommends the Grafana Java agent and `OTEL_*` variables: we use `spring-boot-starter-opentelemetry` and `management.*` properties (see README, Observability).
- `prometheus-label-strategy` tolerates `tenant_id` for small tenant counts: we never tag metrics with tenant or entity ids.
- `promql` / `slo-implementation` examples use `http_requests_total` / `http_request_duration_seconds`: ours are `http_server_requests_seconds_count|bucket` with `status`, `uri`, `outcome`.
- `182-java-observability-metrics-micrometer` instruments services with hand-built meters: features declare business metrics on events only; infrastructure is automatic.
- `183-java-observability-tracing-opentelemetry` uses the OpenTelemetry API for manual spans: we use Micrometer Observation in adapters only; trace propagation through the outbox and NATS is automatic (`traceparent` / `tracestate` headers, linked producer and consumer spans).
- `181-java-observability-logging` configures `logback.xml` and puts correlation ids in the MDC by hand: we use Boot structured ECS logging via properties; `trace.id`/`span.id` are automatic (`writing-code/references/logging.md`).
- `126-java-exception-handling` uses exceptions for validation and Maven commands: business failures are `Result`; exceptions per `writing-code/references/errors.md`; build with `./gradlew`.

## Output Contract

For a feature: the business metrics declared (name, type, unit, tags, description) and their tests. For a review: missing business metrics, hand-written technical telemetry, forbidden tags, telemetry imports in domain/application.
