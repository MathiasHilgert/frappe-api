package com.frappe.platform;

import java.time.Instant;
import java.util.UUID;

/**
 * Envelope every published domain event carries. Records implement it with plain components.
 *
 * <ul>
 *   <li>{@code eventId}: UUIDv7 created once when the event is raised (from the injected {@link IdGenerator}); never changes on retry, so
 *       JetStream and consumer inboxes can deduplicate.
 *   <li>{@code occurredAt}: UTC instant from the injected {@code Clock}.
 *   <li>{@code aggregateId} and {@code aggregateVersion}: consumers discard stale or out-of-order events per aggregate.
 *   <li>{@code eventVersion}: schema version of the payload; part of the NATS subject.
 * </ul>
 */
public interface DomainEvent {

    /**
     * Identity of this occurrence, a UUIDv7 created once and kept across retries.
     *
     * @return the event id
     */
    UUID eventId();

    /**
     * When the change happened, in UTC.
     *
     * @return the occurrence instant
     */
    Instant occurredAt();

    /**
     * The aggregate that changed.
     *
     * @return the aggregate id
     */
    UUID aggregateId();

    /**
     * Version of the aggregate after the change; increases per aggregate so consumers can drop stale events.
     *
     * @return the aggregate version
     */
    long aggregateVersion();

    /**
     * Schema version of the payload.
     *
     * @return the event schema version
     */
    int eventVersion();
}
