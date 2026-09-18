# Domain events

## Naming and shape

- Past-tense `record` implementing `com.frappe.platform.DomainEvent`: `TabClosed`, `PersonSessionOpened`.
- Envelope (from `DomainEvent`): `UUID eventId` (UUIDv7 via `Uuid7.next(clock)`, created once when the event is raised, never on retry), `Instant occurredAt` (UTC, injected `Clock`), `UUID aggregateId`, `long aggregateVersion` (increases per aggregate), `int eventVersion` (payload schema version). Then `tenantId` and only the data consumers need. No entities, no domain objects with behavior.
- Events other modules consume live in the module root package (public); module-private events stay internal.

## Externalizing to NATS (one step)

Annotate the event with Spring Modulith's `@Externalized`; the platform relay does the rest:

```java
package com.frappe.order;

@Externalized
public record TabClosed(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion,
        int eventVersion, UUID tenantId, Money total) implements DomainEvent {}
```

- Subject: `frappe.<module>.<event-kebab>.v<eventVersion>`, derived from package and class name (`frappe.order.tab-closed.v1`). Leave the annotation's value empty; a value fails the publication with a message naming the derived subject.
- Headers: `Nats-Msg-Id` = `eventId` (JetStream drops re-publishes within the 10-minute duplicate window), `Frappe-Event-Type` (`order.tab-closed`), `Frappe-Event-Version`, `Frappe-Aggregate-Id`, `Frappe-Aggregate-Version`, `Frappe-Occurred-At`. Payload: the record as JSON.
- Stream `FRAPPE` (`frappe.>`, limits retention, 7 days, 1 replica) is created or updated at startup; its config lives in `NatsStreamProvisioner`, not on the server.
- `@Externalized` on a class that does not implement `DomainEvent` fails the publication with a message naming the class.
- Config `frappe.nats.*` (defaults in `NatsProperties` only): `url` (`nats://localhost:4222`, compose's NATS; env `FRAPPE_NATS_URL`), `publish-timeout` (5s), `connection-timeout` (2s), `reconnect-wait` (2s), `connection-name`.
- NATS is optional at startup: the API starts with a WARN, keeps reconnecting, and on every (re)connect provisions the stream and resubmits failed externalized publications.

## Publishing (outbox)

- Aggregates register events; the handler publishes the pulled events through Spring's `ApplicationEventPublisher` inside the command transaction.
- Spring Modulith's event publication registry writes each event to the Postgres outbox in that same transaction. All events go through it, including those consumed inside the same module.
- After commit, the relay publishes to JetStream synchronously and the publication is marked complete only after the ack. If NATS is down the publish fails after `publish-timeout` and the publication stays incomplete for resubmission; nothing blocks indefinitely.
- No ordering across instances: consumers order per aggregate with `aggregateVersion`.

## Consuming

- Delivery is at-least-once. Every consumer is idempotent:
  1. In one transaction, insert `eventId` into the module's `inbox` table (unique key).
  2. If the insert conflicts, skip — already processed.
  3. Otherwise apply the effect in the same transaction.
- Consumers call the module's own bus (a command), never another module's internals.
- Never rely on ordering across aggregates; within one aggregate use the event's version or timestamp to discard stale events.

## Changing an event

Events are contracts. Add optional fields only; for breaking changes publish a new event type and migrate consumers first.
