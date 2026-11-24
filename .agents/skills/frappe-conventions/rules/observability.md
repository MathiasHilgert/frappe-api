# Observability (OBS)

- OBS-01 · Modules emit only metrics on their own meter; the platform owns spans and exporters
- OBS-02 · Metric names are `frappe.<module>.<noun>[.<qualifier>]`, declared on the module's metrics class
- OBS-03 · Every instrument has a UCUM unit and a one-sentence description
- OBS-04 · Tags are bounded; the outcome tag is always `frappe.outcome`
- OBS-05 · An enum becomes a tag through an exhaustive switch that throws
- OBS-06 · Business-success counters count only after commit
- OBS-07 · Every feature declares its business metrics
- OBS-08 · New instruments use the metrics source generator and `CountOnCommit`
- OBS-09 · A consumer counts each delivery once, after it settles
- OBS-10 · Gauges that read a store are sampled and cached; metrics never fail the business path
- OBS-11 · The platform wraps every handler in a span; span outcomes reuse the metric vocabulary
- OBS-12 · Trace context crosses the broker through the outbox
- OBS-13 · Sampling is parent-based in the app and tail-based in the collector
- OBS-14 · Every log is `[LoggerMessage]` at the level of what happened
- OBS-15 · Every failure or lag metric ships with a provisioned alert
- OBS-16 · Every alert carries its runbook in its annotations
- OBS-17 · Counter alerts also count series born inside the window
- OBS-18 · Every module has availability and latency SLOs with multi-window burn-rate alerts
- OBS-19 · Every module has a dashboard as code that shows each business metric
- OBS-20 · Liveness checks the process; readiness checks each capability

## OBS-01 · Modules emit only metrics on their own meter; the platform owns spans and exporters

- **Rule:** A module creates no `ActivitySource`, exporter or tracer; it records business metrics on the meter `Frappe.<Module>` obtained from
  `IMeterFactory`. Hosts subscribe to `Frappe.*`.
- **Why:** one place configures telemetry; a module that exports on its own duplicates or loses signals.
- **Enforced by:** `LayeringTests` (Architecture tests).
- **Good:** `Meter = meters.Create(MeterName); // "Frappe.Orders"` **Bad:** `static readonly ActivitySource Source = new("Frappe.Orders");` in a
  module.
- **Status:** in force.

## OBS-02 · Metric names are `frappe.<module>.<noun>[.<qualifier>]`, declared on the module's metrics class

- **Rule:** Names are lower snake case segments `frappe.<module>.<noun>[.<qualifier>]`, held as `const` on `<Module>Metrics` in Application, which
  creates its counters and histograms in its constructor. An observable gauge that reads a store is declared in Infrastructure next to that store, on
  the module's meter, with its name const on `<Module>Metrics`. Never rename a shipped metric: dashboards and alerts read it.
- **Why:** one file lists what a module reports; stable names keep dashboards working.
- **Enforced by:** platform meter test (planned, OBS-08); review.
- **Good:** `public const string SyncRunsMetric = "frappe.orders.sync.runs";` **Bad:** `meter.CreateCounter<long>("OrdersSyncRuns")` inline in a
  handler.
- **Platform features:** a platform piece that answers instead of the handler (idempotency replay, paging refusals) counts its outcomes on the
  `Frappe.Http` meter as `frappe.http.<feature>.requests` tagged `frappe.outcome`.
- **Status:** in force.

## OBS-03 · Every instrument has a UCUM unit and a one-sentence description

- **Rule:** Pass `unit:` (UCUM: `s`, `{request}`, `USD`) and `description:` (one sentence, says what is counted and by which tags) to every
  instrument.
- **Why:** an operator reading a panel must know what one unit means without opening the code.
- **Enforced by:** `MessagingMetricsTests` (messaging only); platform meter test (planned, OBS-08).
- **Good:** `CreateCounter<long>(SyncRunsMetric, unit: "{run}", description: "Reference data sync runs, by source and outcome.")` **Bad:**
  `CreateCounter<long>(SyncRunsMetric)`.
- **Status:** in force.

## OBS-04 · Tags are bounded; the outcome tag is always `frappe.outcome`

- **Rule:** Tag values come from a closed set (enum, source name, region code). Never tag ids, message types, emails, tokens, origins or user-typed
  text. The outcome of anything is tagged `frappe.outcome`.
- **Why:** unbounded tags explode series cardinality in Mimir and leak personal data.
- **Enforced by:** platform meter test with a tag allow-list (planned, OBS-08); review.
- **Good:** `new(OutcomeTag, "refused")` **Bad:** `new("email", command.Email)`.
- **Status:** in force.

## OBS-05 · An enum becomes a tag through an exhaustive switch that throws

- **Rule:** Map each enum value to its tag string in a switch with one arm per value and a throwing discard; never a fallback string.
- **Why:** a new value with a silent default lands under the wrong series and nobody notices.
- **Enforced by:** unit tests of the mapping; review.
- **Good:** `_ => throw new UnreachableException($"Unknown outcome {outcome}.")` **Bad:** `_ => "failed",`
- **Status:** in force.

## OBS-06 · Business-success counters count only after commit

- **Rule:** A counter of a business success (registered, verified, opened, completed) increments only after the unit of work committed: an
  `IAfterCommitDomainEventHandler` today, `CountOnCommit` once it exists (OBS-08). Attempts and rejections count where they happen, tagged with their
  outcome.
- **Why:** a write that loses its race at commit would otherwise be counted as a success.
- **Enforced by:** review; `CountOnCommit` (`src/Platform/Frappe.Observability/CountOnCommitRegistration.cs`) makes the right way the short way.
- **Good:** `CountRegisteredOwner : IAfterCommitDomainEventHandler<OwnerRegistered>` **Bad:** `metrics.SessionOpened(SessionOpening.Login);` in the
  handler body before commit.
- **Status:** in force.

## OBS-07 · Every feature declares its business metrics

- **Rule:** A feature's plan lists the business metrics it adds (name, unit, tags, the question each answers) and the alert or panel that reads each.
  A feature that changes business behavior without one says why in the plan.
- **Why:** metrics added after an incident arrive too late to explain it.
- **Enforced by:** review (critic-observability); dashboard test (OBS-19).
- **Good:** plan line `frappe.billing.owners.verified {owner}: how many registrations finish` **Bad:** a new use case with no metric and no reason.
- **Status:** in force.

## OBS-08 · New instruments use the metrics source generator and `CountOnCommit`

- **Rule:** Declare instruments with the Microsoft.Extensions.Telemetry.Abstractions generator and strongly typed tag structs
  (`[Counter<long>(typeof(Tags), Name = ...)]`, `[TagName]`). Count after-commit successes with the platform `services.CountOnCommit<TEvent,
  TInstrument>(e => tags)` instead of one-off handlers. A platform test walks every `Frappe.*` meter: name `frappe.<module>.*`, unit, description,
  tags from an allow-list. Existing metric names are kept.
- **Why:** typed tags remove misspelled or unbounded tags at compile time; one helper keeps OBS-06 uniform.
- **Enforced by:** `MeterConventionTests` (planned) in `tests/Frappe.Architecture.Tests`; `CountOnCommit` (`CountOnCommitRegistration.cs`).
- **Good:** `[Counter<long>(typeof(SyncRunTags), Name = "frappe.orders.sync.runs")] public static partial SyncRuns CreateSyncRuns(Meter meter);`
  **Bad:**
  `_syncRuns.Add(1, new("frappe.outcome", name), new("source", s));` with hand-typed keys.
- **Status:** in force.

## OBS-09 · A consumer counts each delivery once, after it settles

- **Rule:** Count a delivery after it is acked, nacked or dead-lettered, once, tagged with `handled`, `duplicate`, `failed` or `dead_lettered`.
- **Why:** counting before settlement double-counts redeliveries.
- **Enforced by:** `JetStreamConsumerMetricsTests`.
- **Good:** `metrics.Consumed(consumer, outcome)` after `AckAsync` **Bad:** counting on receipt.
- **Status:** in force.

## OBS-10 · Gauges that read a store are sampled and cached; metrics never fail the business path

- **Rule:** An observable gauge that reads the database reuses one cached reading for a fixed lifetime; on failure it logs and keeps the last value.
  Recording a metric never throws into a use case.
- **Why:** an exporter collecting every 15 s must not load the database, and a telemetry fault must not refuse a request.
- **Enforced by:** `OutboxRelayMetricsTests`; review.
- **Good:** `SyncMetrics.ReadingLifetime = Duration.FromMinutes(1)` **Bad:** a gauge callback that runs a query per collection.
- **Status:** in force.

## OBS-11 · The platform wraps every handler in a span; span outcomes reuse the metric vocabulary

- **Rule:** Spans of use cases and consumers come from the platform pipeline with `frappe.handler.*` tags; only exceptions set error status. A span's
  `frappe.outcome` uses the same values as the matching metric (`handled`, `duplicate`, `failed`, `dead_lettered`).
- **Why:** one vocabulary lets a trace and a panel be joined by outcome.
- **Enforced by:** Platform tests (`TraceFlowTests`); review.
- **Good:** `activity?.SetTag("frappe.outcome", "handled");` **Bad:** `activity?.SetTag("frappe.outcome", "processed");`
- **Status:** in force.

## OBS-12 · Trace context crosses the broker through the outbox

- **Rule:** The outbox stores `traceparent`/`tracestate` of the saving span; publish and consume continue it. Hosts listen to every source in
  `MessagingTelemetry.ActivitySources`.
- **Why:** without both sources the save → publish → consume tree has holes.
- **Enforced by:** `TraceFlowTests`; review.
- **Good:** `traces.AddSource([.. MessagingTelemetry.ActivitySources, ...])` **Bad:** listening to `Frappe.Messaging` but not `NATS.Net`.
- **Status:** in force.

## OBS-13 · Sampling is parent-based in the app and tail-based in the collector

- **Rule:** Processes use a parent-based sampler and send every span to the OTel collector. The collector's `tail_sampling` keeps 100% of errors and
  slow traces plus a configurable share of the rest; the share changes in the collector config, never by redeploying a process.
- **Why:** head sampling drops the error traces you need; tail sampling decides after seeing the whole trace.
- **Enforced by:** review of `deploy/observability/otel-collector.yaml`.
- **Good:** `processors: [memory_limiter, tail_sampling, batch]` with `status_code` and `latency` policies **Bad:** `TraceIdRatioBasedSampler(0.1)` in
  the app.
- **Status:** in force.

## OBS-14 · Every log is `[LoggerMessage]` at the level of what happened

- **Rule:** Log through `[LoggerMessage]` methods with named placeholders. A business rejection is Information, a retryable infrastructure fault
  Warning, a dead-letter, park or unhandled exception Error. Never log payloads, secrets or PII.
- **Why:** structured fields feed Loki queries and alerts; levels decide who gets woken.
- **Enforced by:** CA1848 (suggestion today); review.
- **Good:** `[LoggerMessage(Level = LogLevel.Information, Message = "{Kind} {UseCase} was rejected: {ErrorCodes}")]` **Bad:**
  `logger.LogError($"rejected {command}")`.
- **Status:** in force.

## OBS-15 · Every failure or lag metric ships with a provisioned alert

- **Rule:** A metric of a failure or lag state (parked, oldest age, dead-lettered, freshness, refused configuration) ships in the same PR with a
  Grafana rule in `deploy/observability/grafana/provisioning/alerting/frappe-alerts.yaml`: datasource `loki` or `mimir` only, no `$$`, `noDataState:
  OK`, its `uid` listed in `AlertProvisioningTests`.
- **Why:** a failure metric nobody alerts on is discovered by users.
- **Enforced by:** `AlertProvisioningTests` (planned).
- **Good:** `- uid: frappe-mail-failed-configuration` plus the uid in the test's expected list **Bad:** `frappe.messaging.outbox.parked` with no rule.
- **Status:** in force.

## OBS-16 · Every alert carries its runbook in its annotations

- **Rule:** Each rule has `summary`, `description` and `runbook` annotations; `runbook` says what to look at, what to do, and the exact SQL or
  command. Operational knowledge with no alert to hang on is dropped.
- **Why:** the runbook is read at the moment the alert fires, not searched for in a wiki.
- **Enforced by:** `AlertProvisioningTests.Every_rule_carries_a_summary_a_description_and_a_runbook` (planned).
- **Good:** `runbook: Read the failure in the Worker's logs ... replay them to their stream once the fix is live.` **Bad:** an alert with only
  `summary`.
- **Status:** in force.

## OBS-17 · Counter alerts also count series born inside the window

- **Rule:** An alert on a counter adds an arm that counts series present now and absent at the window start, since `increase()` misses a series born
  at 1.
- **Why:** the first failure after a restart is otherwise invisible.
- **Enforced by:** review.
- **Good:** `increase(x[15m]) > 0 or (x unless x offset 15m)` **Bad:** `increase(x[15m]) > 0` alone.
- **Status:** in force.

## OBS-18 · Every module has availability and latency SLOs with multi-window burn-rate alerts

- **Rule:** Each module defines an availability SLO and a latency SLO over the existing HTTP server metrics, filtered by its routes, and ships
  multi-window, multi-burn-rate alerts as code (fast: 1 h and 5 m at 14.4×; slow: 6 h and 30 m at 6×) in the provisioned alert file.
- **Why:** burn-rate alerts page on budget at risk, not on every blip.
- **Enforced by:** `AlertProvisioningTests` (planned) (expected rules list an availability and a latency fast/slow pair for each module with
  routes: Billing and Orders by `/v1/{module}/`, Shipping by its flat prefix `/v1/business.*` in its own test;
  a module with flat routes selects its route prefix, see
  HTTP-02).
- **Good:** `uid: frappe-orders-availability-fast-burn` **Bad:** `5xx > 0` as the only HTTP alert.
- **Status:** in force.

## OBS-19 · Every module has a dashboard as code that shows each business metric

- **Rule:** Each module has a versioned Grafana JSON under `deploy/observability/grafana/provisioning/dashboards/` with traffic, errors, latency,
  business metrics and messaging rows, provisioned at startup. A test fails when a business metric of the module appears in no panel query.
- **Why:** a metric no panel shows is not watched.
- **Enforced by:** `DashboardProvisioningTests.Every_business_metric_of_the_module_is_in_a_panel_query` (planned).
- **Good:** `dashboards/billing.json` querying `frappe_billing_owners_registered_total` **Bad:** a dashboard built by hand in the UI.
- **Status:** in force.

## OBS-20 · Liveness checks the process; readiness checks each capability

- **Rule:** Liveness reports only that the process runs. Readiness registers each dependency with `AddCriticalDependency` or `AddOptionalDependency`,
  with a 3 s timeout.
- **Why:** a liveness probe that checks Postgres restarts every replica during a database blip.
- **Enforced by:** `WorkerCompositionTests`.
- **Good:** `services.AddCriticalDependency("nats", provider => new NatsHealthCheck(...))` **Bad:** a database check tagged `live`.
- **Status:** in force.
