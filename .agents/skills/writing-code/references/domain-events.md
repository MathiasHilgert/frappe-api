# Domain events

## Naming and shape

- Past-tense `record`: `TabClosed`, `PersonSessionOpened`.
- Fields: `EventId eventId` (UUIDv7), IDs of affected aggregates, `tenantId`, `occurredAt` (`Instant`), and only the data consumers need. No entities, no domain objects with behavior.
- Events other modules consume live in the module root package (public); module-private events stay internal.

## Publishing (outbox)

- Aggregates register events; the handler publishes the pulled events through Spring's `ApplicationEventPublisher` inside the command transaction.
- Spring Modulith's event publication registry (JPA) writes each event to the Postgres outbox in that same transaction. All events go through it, including those consumed inside the same module.
- The outbox relay publishes committed rows to NATS JetStream. Subject: `frappe.<module>.<event-name-kebab>` (e.g. `frappe.order.tab-closed`).

## Consuming

- Delivery is at-least-once. Every consumer is idempotent:
  1. In one transaction, insert `eventId` into the module's `inbox` table (unique key).
  2. If the insert conflicts, skip — already processed.
  3. Otherwise apply the effect in the same transaction.
- Consumers call the module's own bus (a command), never another module's internals.
- Never rely on ordering across aggregates; within one aggregate use the event's version or timestamp to discard stale events.

## Changing an event

Events are contracts. Add optional fields only; for breaking changes publish a new event type and migrate consumers first.
