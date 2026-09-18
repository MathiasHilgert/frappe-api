package com.frappe.platform;

import java.time.Instant;
import java.util.UUID;

/**
 * Envelope every published domain event carries. Records implement it with plain components.
 *
 * <ul>
 *   <li>{@code eventId}: UUIDv7 created once when the event is raised ({@link Uuid7#next}); never changes on retry, so
 *       JetStream and consumer inboxes can deduplicate.
 *   <li>{@code occurredAt}: UTC instant from the injected {@code Clock}.
 *   <li>{@code aggregateId} and {@code aggregateVersion}: consumers discard stale or out-of-order events per aggregate.
 *   <li>{@code eventVersion}: schema version of the payload; part of the NATS subject.
 * </ul>
 */
public interface DomainEvent {

    UUID eventId();

    Instant occurredAt();

    UUID aggregateId();

    long aggregateVersion();

    int eventVersion();
}
