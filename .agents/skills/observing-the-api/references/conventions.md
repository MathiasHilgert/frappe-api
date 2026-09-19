# Metric and span conventions

## What to measure

- Golden signals per service/endpoint (automatic): latency, traffic, errors, saturation. RED for request-driven paths (`http_server_requests_seconds`: rate, errors via `outcome`/`status`, duration histogram); USE for resources (`hikaricp_connections_*`, `jvm_*`, `executor_*`).
- Business metrics (declared): one per meaningful domain outcome the business asks about: how many (counter), how much / how long (distribution). Derive them from the event that records the outcome, never from a controller or handler.
- Prefer a counter with a bounded `outcome`-like enum over two counters (`orders.placed{channel}` rather than `orders.placed.dine_in`).

## Choosing the meter

| Question | Declaration |
| --- | --- |
| How many times did X happen? | `@Counted` |
| How much money / how many items per X? | `@Measured(unit = MONEY / ITEMS)` |
| How long did X take (business time, e.g. tab open → closed)? | `@Measured(unit = SECONDS)` on a `Duration` field (histogram buckets) |
| Current state (open tabs now) | not an event metric: derive from counters (`opened - closed`) or ask for a platform gauge in a ticket |

## Naming

- `frappe.<module>.<plural-noun>.<past-participle>`; the module prefix is derived from the event's package.
- Micrometer names are dotted; Prometheus converts dots to `_`, appends the base unit and `_total` for counters (`frappe_order_tabs_revenue_minor_units_sum`).
- Units are base units: seconds, bytes, minor currency units, items. Never milliseconds or cents-as-double.

## Tags (metrics) vs attributes (spans)

| Value | Metric tag | Span attribute / log field |
| --- | --- | --- |
| Enum or boolean of the event (`channel`, `split`) | yes | yes |
| `currency` | automatic for money | yes |
| Tenant, branch, table, user, aggregate, event ids | never | yes (high-cardinality key values, `frappe.*` log fields) |
| Free text, amounts, timestamps | never | only if not personal data |

Series count = product of tag cardinalities; keep each business metric under ~50 series.

## Use cases (RED per use case, automatic)

- Every call of a `@CommandUseCase` / `@QueryUseCase` operation is one observation `use_case`: the timer `use_case` (Prometheus `use_case_seconds_count|sum|bucket`) gives rate, errors and duration per use case, and the span `<module> <UseCase>` wraps the use case including its commit.
- Use cases live in internal `application` packages, which Spring Modulith does not observe, so there is no duplicate span: a call through a module's `Api` or a controller shows Modulith's module-entry span as the parent of the `<module> <UseCase>` span (entry versus operation).
- Tags: `use_case.name` (use case class, e.g. `CloseTab`), `use_case.module`, `use_case.kind` (`command` | `query`), `outcome`, plus Micrometer's `error` (exception simple name, `none` otherwise). All bounded by the code base.
- `outcome=failure` is a business refusal (a returned `Result.Failure`), `outcome=error` a defect or infrastructure fault (an exception, including a failed commit). Alert on `error`; track `failure` as product signal.

## Spans

- Automatic spans: `http <method> <route>` (server/client), `connection`/`query`/`result-set` (JDBC), Spring Modulith module entries and cross-module listeners, `publish <subject>` (NATS, PRODUCER), `process <subject>` (NATS, CONSUMER), and `<module> <UseCase>` for every use case call. Every scheduled task execution is `scheduled task <name>` (timer `scheduled.task`, tags `scheduled.task.name`, `scheduled.task.outcome` = `success` | `failure`, `error`; long task timer `scheduled.task.active`, tag `scheduled.task.name`), a root span with the task's queries as children.
- Across the outbox and the broker, spans **link** to the trace the event was recorded in (its creation context, carried in `traceparent` / `tracestate`); they never continue it, because delivery is at least once and may be late. In Tempo, follow the link from the consumer span back to the command that caused the event.
- New infrastructure adapters add one Micrometer `Observation` at the adapter boundary (see `NatsPublishObservation`): name `<technology>.<operation>`, contextual name per OpenTelemetry semantic conventions, low-cardinality keys only for bounded values, ids as high-cardinality keys, `error(...)` on failure, stop in `finally`.

## Sampling and export

- `local` samples 100%; elsewhere `FRAPPE_TRACING_SAMPLING_PROBABILITY` (default 0.1, parent-based).
- OTLP endpoints come from the environment (README, Observability). Without them the API runs and drops telemetry.
