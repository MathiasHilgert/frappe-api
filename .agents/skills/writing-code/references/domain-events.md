# Domain events

## Naming and shape

- Past-tense `record` implementing `com.frappe.platform.DomainEvent`: `TabClosed`, `PersonSessionOpened`.
- Envelope (from `DomainEvent`): `UUID eventId` (UUIDv7 from the injected `IdGenerator`, created once when the event is raised, never on retry), `Instant occurredAt` (UTC, injected `Clock`), `UUID aggregateId`, `long aggregateVersion` (increases per aggregate), `int eventVersion` (payload schema version). Then `tenantId` and only the data consumers need. No entities, no domain objects with behavior.
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

- Aggregates register events; the command handler saves the aggregate, then hands the pulled events to the kernel port `com.frappe.platform.DomainEventPublisher`, inside the command transaction. Never inject Spring's `ApplicationEventPublisher` in application code.

```java
@Transactional
public Result<TabId, TabError> handle(CloseTab cmd) {
    return tabs.byId(cmd.tabId())
            .flatMap(tab -> tab.close(clock))
            .map(tab -> {
                tabs.save(tab);
                events.publishAll(tab.pullEvents()); // stored with the aggregate, or not at all
                return tab.id();
            });
}
```

- The adapter (`platform.infrastructure.events`) is `@Transactional(propagation = MANDATORY)`: publishing outside a transaction throws `IllegalTransactionStateException`, because the event could not reach the outbox atomically.
- Spring Modulith's JDBC event publication registry writes one row per interested listener to `platform.event_publication` in that transaction; rollback leaves no row. All events go through it, including those consumed inside the same module. The tables are the official Modulith 2.1.1 Postgres DDL, created by Flyway (`db/migration/platform`); `spring.modulith.events.jdbc.schema-initialization.enabled=false`.
- After commit, the relay publishes to JetStream synchronously and the publication completes only after the ack. Completion mode `ARCHIVE` then moves the row to `platform.event_publication_archive` (purging the archive is a follow-up). If NATS is down the publish fails after `publish-timeout` and the row stays in `event_publication` as `FAILED`; nothing blocks indefinitely.
- Recovery has two paths. Both can deliver an event twice (an attempt judged stuck that was only slow, two instances): JetStream drops duplicates by `Nats-Msg-Id` only within its 10-minute window; after that the consumer inbox on `eventId` is the guarantee.
  1. Every NATS (re)connect resubmits failed externalized publications.
  2. `frappe.outbox.recovery.*` (defaults in `OutboxRecoveryProperties`: `interval` 1m, `batch-size` 100, `stuck-after` 5m, `max-attempts` 24, `max-backoff` 1h) runs on a fixed delay:
     1. Fails attempts stuck without outcome, judged by `last_resubmission_date` or, for a first attempt, `publication_date`. Keep `stuck-after` above `frappe.nats.publish-timeout` plus the slowest listener.
     2. Moves failed publications with `completion_attempts >= max-attempts` (`MAX_ATTEMPTS_EXHAUSTED`) or whose event class is gone from the classpath (`UNKNOWN_EVENT_TYPE`; the registry would skip them forever) to `platform.event_publication_dead_letter` and logs each once at ERROR (`frappe.outbox.publication_id`, `event_type`, `listener_id`, `completion_attempts`, `dead_letter_reason`).
     3. Resubmits failed publications whose backoff elapsed (`interval` doubling per attempt, capped at `max-backoff`; about 18 hours of retries with the defaults), least recently attempted first, at most `batch-size` in flight. A publication that keeps failing waits longer and goes to the back, so it never starves newer failures. A selected publication whose JSON no longer deserializes is dead-lettered (`UNREADABLE_PAYLOAD`) without affecting the rest of the batch; the reconnect path skips it.
     4. Refreshes the gauge `frappe.outbox.dead.letters`; alert on any value above zero.
- Spring Modulith's staleness monitor (`spring.modulith.events.staleness.*`) stays off: it judges every status by `publication_date`, so it would fail an old event in the middle of its resubmission and cause concurrent duplicate runs.
- `republish-outstanding-events-on-restart` stays off (Modulith issue #526; it would resubmit in-flight publications of every instance).
- No ordering across instances: consumers order per aggregate with `aggregateVersion`.


### Dead letters: manual replay

Fix the cause first (deploy the missing consumer, restore the event class, bring NATS back). Then move the row back as `frappe_app`; the next recovery run resubmits it with a fresh attempt budget:

```sql
with replay as (
    delete from platform.event_publication_dead_letter where id = :publication_id returning *)
insert into platform.event_publication (id, listener_id, event_type, serialized_event, publication_date, status,
    completion_attempts)
select id, listener_id, event_type, serialized_event, publication_date, 'FAILED', 0 from replay;
```

Drop `where id = ...` to replay all, or filter by `reason` / `event_type`. To discard a dead letter, delete it and record why in the incident.

## Consuming

- Delivery is at-least-once. Every consumer is idempotent:
  1. In one transaction, insert `eventId` into the module's `inbox` table (unique key).
  2. If the insert conflicts, skip — already processed.
  3. Otherwise apply the effect in the same transaction.
- Consumers call the module's own bus (a command), never another module's internals.
- Never rely on ordering across aggregates; within one aggregate use the event's version or timestamp to discard stale events.

## Changing an event

Events are contracts. Add optional fields only; for breaking changes publish a new event type and migrate consumers first.
