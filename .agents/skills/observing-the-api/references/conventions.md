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

## Spans

- Automatic spans: `http <method> <route>` (server/client), `connection`/`query`/`result-set` (JDBC), Spring Modulith module entries and cross-module listeners, `publish <subject>` (NATS).
- New infrastructure adapters add one Micrometer `Observation` at the adapter boundary (see `NatsPublishObservation`): name `<technology>.<operation>`, contextual name per OpenTelemetry semantic conventions, low-cardinality keys only for bounded values, ids as high-cardinality keys, `error(...)` on failure, stop in `finally`.

## Sampling and export

- `local` samples 100%; elsewhere `FRAPPE_TRACING_SAMPLING_PROBABILITY` (default 0.1, parent-based).
- OTLP endpoints come from the environment (README, Observability). Without them the API runs and drops telemetry.
