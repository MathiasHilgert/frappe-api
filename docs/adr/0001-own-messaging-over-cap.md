# ADR 0001: Keep our own messaging capability instead of DotNetCore.CAP

- **Status:** accepted (2026-11-08)
- **Context owner:** platform (`src/Platform/Frappe.Messaging`)

## Context

Frappé publishes integration events through a transactional outbox per module, relays them to NATS JetStream as
CloudEvents, and consumes them through an inbox written in the same transaction as the handler's effect. An
independent review of the codebase asked whether hand-rolling this (about 1,300 lines plus tests) was justified,
and pointed out that DotNetCore.CAP supports NATS JetStream with a PostgreSQL outbox. MediatR and MassTransit went
commercial in 2025, so the usual alternatives were already out.

We ran a time-boxed spike: CAP 10.0.2 (`DotNetCore.CAP.NATS`, PostgreSQL storage) on .NET 11 RC3, against real
Postgres (`postgis/postgis:18-3.7-alpine`) and NATS 2.11 with JetStream, probing each guarantee our tests pin.

## Findings

| Requirement | CAP 10.0.2 | Observed |
|-------------|------------|----------|
| Outbox in the business transaction | Opt-in only | A plain EF transaction rolled back, yet the message was published and handled |
| Commit with the broker down | Unsafe | `CommitAsync` threw after the business data was committed; with a paused broker it blocked about 9 s inline |
| Outage vs rejected message | Not distinguished | One retry counter: an outage of about 50 min (defaults) marks healthy messages permanently Failed, even ones later delivered |
| Consumer idempotency (inbox) | Not provided | The same message twice ran the handler twice; CAP acks before the handler runs |
| Message from a non-CAP producer | Unbounded loop | About 101,000 redeliveries in 96 s |
| Schema per module | One schema per host | A second `AddCap` overrides the first; without `CREATE` rights the host starts with no tables and only logs |
| NATS client | Legacy | Depends on `NATS.Client 1.1.8`, not NATS.Net |
| Single trace save → consume | Supported | Works within one process with `AddCapInstrumentation()` |

## Decision

Keep `Frappe.Messaging`. Adopting CAP would replace about 700 lines (interceptor, relay, publisher, consume loop),
but we would still write the inbox, the CloudEvents mapping, the topology and dead-lettering, and we would lose the
behaviours that our tests prove: outage versus rejection, bounded retries with a dead-letter stream, heartbeat and
handler deadline, and the outbox written automatically on `SaveChanges`.

## Consequences

- We own delivery semantics, so they stay covered by integration tests against real Postgres and NATS.
- We revisit this decision if a maintained library covers: an inbox in the handler's transaction, outage versus
  rejection classification, schema per module with least-privilege roles, and NATS.Net.
