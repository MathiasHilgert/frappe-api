# SLIs, SLOs and alerting

## SLIs (ratios of good events to valid events)

| SLI | Good / valid (PromQL, 28d or the alert window) |
| --- | --- |
| Availability | `sum(rate(http_server_requests_seconds_count{outcome!="SERVER_ERROR"}[w])) / sum(rate(http_server_requests_seconds_count[w]))` |
| Latency | `sum(rate(http_server_requests_seconds_bucket{le="0.3"}[w])) / sum(rate(http_server_requests_seconds_count[w]))` |
| Event delivery | `sum(rate(nats_publish_seconds_count{error="none"}[w])) / sum(rate(nats_publish_seconds_count[w]))` |
| Business flow | from business counters, e.g. payments captured / payments attempted |

Exclude `/actuator/**` from user-facing SLIs (`uri!~"/actuator.*"`).

## SLO buckets

Latency SLOs need a histogram bucket at the threshold. HTTP server histograms are on by default; configure explicit SLO buckets per meter with `management.metrics.distribution.slo.<meter-name>=100ms,300ms,1s` (e.g. `http.server.requests`, `frappe.order.tabs.duration`). Pick thresholds from the user journey, not from current performance.

## Targets (starting points, revisit with data)

- Availability 99.9% per 28 days (≈40 min budget); latency 95% of requests under 300 ms for commands, 200 ms for queries.
- One SLO per user-facing capability, owned by the module that implements it; document it in the feature ticket.

## Burn-rate alerting (multi-window, multi-burn-rate)

| Severity | Long window | Short window | Burn rate | Budget spent |
| --- | --- | --- | --- | --- |
| Page | 1h | 5m | 14.4 | 2% in 1h |
| Page | 6h | 30m | 6 | 5% in 6h |
| Ticket | 3d | 6h | 1 | 10% in 3d |

Alert when both windows exceed `burn_rate × (1 − SLO)`. Never alert on raw CPU or single errors; alert on budget burn. Details and templates: vendored `slo-implementation`; PromQL mechanics: vendored `promql`.
