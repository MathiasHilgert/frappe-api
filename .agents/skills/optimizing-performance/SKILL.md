---
name: optimizing-performance
description: "Guides Frappé performance work: JVM and Spring tuning, virtual threads, AOT cache, JFR, query shape, Postgres indexes, RLS, outbox and pool sizing. Use for slow endpoints, slow queries, memory or startup issues."
license: Proprietary
metadata:
  author: "MathiasHilgert"
  version: "1.0"
---

## Activation Contract

Load when a ticket or report mentions latency, throughput, memory, startup time, slow queries, connection exhaustion, or when adding a read path over large data.

## Hard Rules

- Measure before and after; report numbers with how they were obtained. Everything in the references is guidance, not a measured fact for this codebase.
- Never trade correctness, tenancy (RLS) or event delivery guarantees for speed.
- Never bypass the domain or outbox to save a query on the write side.
- One optimization per commit, with its measurement in the message body or PR.

## Decision Gates

| Symptom or area | Reference |
| --- | --- |
| CPU, memory, threads, startup, observability, ORM query count, pagination | `references/jvm-and-spring.md` |
| Slow SQL, indexes, RLS cost, outbox growth, connection pool | `references/database.md` |

## Execution Steps

1. Reproduce with a test or a request trace; capture a baseline (Micrometer/OTel metrics, JFR, `EXPLAIN (ANALYZE, BUFFERS)`).
2. Locate the bottleneck from evidence, not intuition.
3. Apply the smallest change from the matching reference.
4. Re-measure under the same conditions; keep the change only if it helps.
5. Run `./gradlew check`.

## Output Contract

Return: symptom, baseline, change, result (same method), and remaining risks or follow-ups.

## References

- `references/jvm-and-spring.md`
- `references/database.md`
