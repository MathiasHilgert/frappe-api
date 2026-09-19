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
- Headers: `Nats-Msg-Id` = `eventId` (JetStream drops re-publishes within the 10-minute duplicate window), `Frappe-Event-Type` (`order.tab-closed`), `Frappe-Event-Version`, `Frappe-Aggregate-Id`, `Frappe-Aggregate-Version`, `Frappe-Occurred-At`, plus `traceparent` / `tracestate` (see "Trace context" below). Payload: the record as JSON.
- Stream `FRAPPE` (`frappe.>`, limits retention, 7 days, 1 replica) is created or updated at startup; its config lives in `NatsStreamProvisioner`, not on the server.
- `@Externalized` on a class that does not implement `DomainEvent` fails the publication with a message naming the class.
- Config `frappe.nats.*` (defaults in `NatsProperties` only): `url` (`nats://localhost:4222`, compose's NATS; env `FRAPPE_NATS_URL`), `publish-timeout` (5s), `connection-timeout` (2s), `reconnect-wait` (2s), `connection-name`.
- NATS is optional at startup: the API starts with a WARN, keeps reconnecting, and on every (re)connect provisions the stream and publishes `MessagingTransportRecovered` (platform infrastructure), which triggers an immediate outbox recovery pass.

## Publishing (outbox)

- Aggregates register events; the command use case saves the aggregate, then hands the pulled events to the kernel port `com.frappe.platform.DomainEventPublisher`, inside the command transaction. Never inject Spring's `ApplicationEventPublisher` in application code.

```java
@Transactional
public Result<TabId, TabError> close(TabId tabId) {
    return tabs.byId(tabId)
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
- After commit, the relay publishes to JetStream synchronously and the publication completes only after the ack. Completion mode `ARCHIVE` then moves the row to `platform.event_publication_archive`, purged after `frappe.outbox.archive.retention` (see "Archive purge" below). If NATS is down the publish fails after `publish-timeout` and the row stays in `event_publication` as `FAILED`; nothing blocks indefinitely.
- Every publish is observed once in the transport (`nats.publish`: a span `publish <subject>` and a timer tagged `messaging.system`, `messaging.destination.name`, `messaging.operation.type` (`send`), `messaging.operation.name` (`publish`), `error`; the event id is a span attribute only). Do not add telemetry around publishing elsewhere.
- Recovery has two paths. Both can deliver an event twice (an attempt judged stuck that was only slow, two instances): JetStream drops duplicates by `Nats-Msg-Id` only within its 10-minute window; after that the consumer inbox on `eventId` is the guarantee.
  1. Every NATS (re)connect triggers the recovery pass below at once, ignoring the backoff (the failures were most likely the outage) but bounded by `batch-size`: `OutboxRecoveryTrigger` hands it to Spring Boot's application task executor (no database I/O on the NATS setup thread), which moves the pending pass to now with trigger `TRANSPORT_RECOVERED`. If a pass is running, the trigger is dropped: that pass covers the same rows.
  2. `frappe.outbox.recovery.*` (defaults in `OutboxRecoveryProperties`: `interval` 1m, `batch-size` 100, `stuck-after` 5m, `max-attempts` 24, `max-backoff` 1h) runs on a fixed delay as the scheduled task `platform.outbox-recovery` (`OutboxRecoveryTask`, see `scheduling.md`): one execution for the whole cluster, so passes never overlap on one instance or across instances, without a lock. Its stored data is the trigger of the next pass.
     1. Fails attempts stuck without outcome, judged by `last_resubmission_date` or, for a first attempt, `publication_date`. Keep `stuck-after` above `frappe.nats.publish-timeout` plus the slowest listener.
     2. Moves failed publications with `completion_attempts >= max-attempts` (Modulith stores 1 on publish and adds one per resubmission, so `max-attempts` counts every attempt, the first publish included) (`MAX_ATTEMPTS_EXHAUSTED`) to `platform.event_publication_dead_letter` and logs each once at ERROR (`frappe.outbox.publication_id`, `event_type`, `listener_id`, `completion_attempts`, `dead_letter_reason`).
     3. Loads at most one batch (minus publications still in flight) of failed publications whose backoff elapsed (`interval` doubling per attempt, capped at `max-backoff`; about 18 hours of retries with the defaults), least recently attempted first, and resubmits each with `PublicationRedelivery`: the same guarded claim (`markResubmitted`) and listener call (`processEvent`) Modulith uses, since Modulith has no public API to resubmit a chosen, bounded set. A publication that keeps failing waits longer and goes to the back, so it never starves newer failures. One that can never be delivered is dead-lettered without affecting the rest of the batch: `UNKNOWN_EVENT_TYPE` (class renamed or deleted), `UNREADABLE_PAYLOAD` (JSON no longer fits), `UNKNOWN_LISTENER` (listener removed). An asynchronous listener failure of a resubmitted publication (the NATS relay) is accepted to leave the row RESUBMITTED until `stuck-after` (default 5m) releases it, because Modulith's in-progress tracking is internal; it then retries with backoff. `ModulithRegistryContractIntegrationTests` pins the Modulith behaviors `PublicationRedelivery` mirrors; revisit them on every Modulith upgrade.
     4. Refreshes the gauge `outbox.dead.letters`; alert on any value above zero.
   Every pass runs inside its `scheduled.task` observation and is observed as `outbox.recovery` (span and timer tagged `outbox.recovery.trigger`: `scheduled`, `transport_recovered`; `error` on a database failure) and every handed-over publication as `outbox.redelivery` (tagged `outbox.redelivery.outcome`; the publication id is a span attribute only). Automatic infrastructure telemetry: do not add more around the outbox.
- Spring Modulith's staleness monitor (`spring.modulith.events.staleness.*`) stays off: it judges every status by `publication_date`, so it would fail an old event in the middle of its resubmission and cause concurrent duplicate runs.
- `republish-outstanding-events-on-restart` stays off (Modulith issue #526; it would resubmit in-flight publications of every instance).
- No ordering across instances: consumers order per aggregate with `aggregateVersion`.


### Trace context

The trace that caused an event survives the outbox and the broker (OpenTelemetry messaging semantic conventions: the message carries its *creation context*, later spans link to it).

- When an externalized event is recorded, the outbox adapter stores the active W3C trace context with it (`platform.event_trace_context`, keyed by `eventId`, written in the same transaction, so it commits and rolls back with the publication). No active trace, no row.
- Every publish reads it and sets the `traceparent` / `tracestate` headers, the first attempt and every resubmission alike: an event delivered after an outage still carries the trace that caused it. A failing lookup logs one WARN and publishes without the headers; telemetry never fails a publish.
- `nats.publish` is a PRODUCER span, a child of whatever runs the publish (the relay listener, or the recovery pass), that **links** to the creation context. Links, not parents, across the outbox: delivery is at least once and may be hours late, and a parent relation would stretch and pollute the producing trace.
- Platform's NATS subscription/inbox adapter wraps processing in `NatsProcessObservations.of(message)`: a CONSUMER span `process <subject>` and `nats.process` timer (`messaging.operation.type` and `.name` both `process`) linked to the same creation context. A message without the headers is processed without a link; a malformed header is ignored (first occurrence WARN, then DEBUG).
- Nothing else is hand-written: no telemetry types in `domain` or `application`, and the W3C values are produced by the configured Micrometer `Propagator`, never by hand.
- Retention: a stored context may be purged only when its `event_id` is in none of `platform.event_publication`, `platform.event_publication_archive` and `platform.event_publication_dead_letter`, because a manually replayed dead letter must still carry its original context. The purge below selects by that rule, not by age.
- Each of those three tables carries a generated, indexed `event_id uuid` column (`V202609191930__add_event_id_to_outbox_tables.sql`), computed by `platform.safe_event_id(serialized_event)` and never written by hand: `serialized_event` is JSON from Modulith's own `EventSerializer`, and `eventId` is a mandatory `DomainEvent` component, so in every path this application controls it is always present, a valid UUID, under its record component name. `safe_event_id` returns null instead of raising for a row this application does not control (malformed JSON, or a non-UUID/missing `eventId`, from a predates-this-migration row or a hand edit): a stored publication must never fail its own insert and so roll back the business transaction that wrote it. The purge matches by equality on this column, not by scanning `serialized_event` with `LIKE`: at outbox scale a per-row text scan of three tables is an unbounded, unindexable cost that would hold the archive purge's transaction open far too long and compete with publishing (the "purging never blocks publishing" invariant).

### Archive purge

`frappe.outbox.archive.*` (defaults in `OutboxArchivePurgeProperties`: `retention` 30d, `purge-batch-size` 500, `purge-interval` 1h) runs as the scheduled task `platform.purge-event-archive` (`OutboxArchivePurgeTask`, see `scheduling.md`): one execution for the whole cluster, so runs never overlap on one instance or across instances, without a lock.

- Deletes archived publications (`platform.event_publication_archive`) whose `completion_date` is older than `retention`, oldest first, in batches of `purge-batch-size`; `OutboxArchivePurger` keeps deleting batches until one comes back smaller than the batch size, so the purge never holds one long-running transaction or lock.
- Trace context is purged in two passes, both by equality on the indexed `event_id` column (see "Trace context" above), never by scanning `serialized_event`: first, every archive batch just deleted drives one bulk delete of its events' trace context (`OutboxArchivePurgeRepository.purgeTraceContextFor`), skipped if any of the three outbox tables still needs it; then a bounded, batched sweep (`purgeOrphanTraceContext`) catches trace context rows left behind without a matching archive delete this run (for example a run that reached the archive delete but was interrupted before purging that batch's trace context; the next run's sweep removes it). Pending publications (`platform.event_publication`) and dead letters are out of scope: dead letters are replayed or discarded by hand (see below), never purged automatically.
- The action throws on failure, never catches to log; the scheduler retries it with backoff (`scheduling.md`). The purged archive and trace context counts so far are still recorded on the observation when a later phase of the same run fails.
- Observed once per run as `outbox.purge` (span and timer; `outbox.purge.archived.count` and `outbox.purge.trace_context.count` are span attributes only, unbounded per run). Automatic infrastructure telemetry: do not add more around the purge. No business metrics.

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
- Consumers call the module's own command use case, never another module's internals.
- A listener touching tenant-scoped tables binds the event's `tenantId` with `TenantScope` and then calls the use case, which opens the transaction the tenant is set in. Binding inside a running transaction throws, so such a listener opens none itself: `@ApplicationModuleListener(propagation = Propagation.NOT_SUPPORTED)`, then `tenants.runAs(event.tenantId(), () -> closeTab.close(event.tabId()))` (`persistence.md`, Tenancy and RLS). `TenantListenerRulesTests` fails the build for a listener that uses `TenantScope` without `NOT_SUPPORTED`.
  - With `NOT_SUPPORTED` the publication behaves as with any listener: the registry marks it completed after the method returns (its own statement, outside the use case's transaction), and a thrown exception leaves it incomplete for the recovery pass to resubmit. The use case's own transaction commits before completion, so a crash in between delivers the event again: the inbox on `eventId` covers it.
  - Forgetting `NOT_SUPPORTED` keeps the default `REQUIRES_NEW` transaction, so `callAs` throws `IllegalStateException` on the first delivery and on every retry; the publication stays incomplete until it is dead-lettered (`MAX_ATTEMPTS_EXHAUSTED`). Find such publications with:

    ```sql
    select id, listener_id, status, completion_attempts, publication_date, last_resubmission_date
    from platform.event_publication
    where completion_date is null and listener_id like '%<ListenerClass>%'
    order by publication_date;
    ```
- `NatsProcessObservations.of(message)` (see "Trace context") is the hook for platform's NATS subscription/inbox adapter, which wraps every processed message in it. Module consumers reach it through that adapter, never directly: it stays package-private in `platform.infrastructure.nats` until the inbox ticket. Do not create spans or timers for consuming by hand.
- Never rely on ordering across aggregates; within one aggregate use the event's version or timestamp to discard stale events.

## Changing an event

Events are contracts. Add optional fields only; for breaking changes publish a new event type and migrate consumers first.
