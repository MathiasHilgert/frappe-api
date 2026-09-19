package com.frappe.identity.domain;

import com.frappe.platform.Counted;
import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.UUID;

/**
 * A start was accepted but mailed nothing: its address had reached the issue cap. Counted only, so abuse against one
 * address becomes visible; it names no address or subject, and no listener consumes it, so no outbox row is written.
 * There is no aggregate: {@link #aggregateId()} is the event's own id.
 *
 * @param eventId the event's id
 * @param occurredAt when the start was capped
 * @param aggregateId the event's id again, since nothing was recorded
 * @param aggregateVersion always 0
 * @param eventVersion the payload's schema version
 */
@Counted(
        name = "sign_ups.capped",
        description = "Sign-up starts accepted without a code because the address was capped")
public record SignUpCapped(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
        implements DomainEvent {

    /** The current payload schema version. */
    public static final int VERSION = 1;

    /**
     * The event of one capped start.
     *
     * @param eventId the event's id
     * @param occurredAt when the start was capped
     * @return the event
     */
    public static SignUpCapped of(UUID eventId, Instant occurredAt) {
        return new SignUpCapped(eventId, occurredAt, eventId, 0, VERSION);
    }
}
