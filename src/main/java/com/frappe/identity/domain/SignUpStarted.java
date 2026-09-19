package com.frappe.identity.domain;

import com.frappe.platform.Counted;
import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.UUID;

/**
 * A sign-up was started or restarted: its code is to be mailed. Internal to identity. It carries the sign-up id only
 * ({@link #aggregateId()}), never the address or the code, because the outbox and its archive keep events for 30 days.
 *
 * @param eventId the event's id
 * @param occurredAt when the sign-up started
 * @param aggregateId the sign-up's id
 * @param aggregateVersion the sign-up's version this event produced
 * @param eventVersion the payload's schema version
 */
@Counted(name = "sign_ups.started", description = "Sign-ups started or restarted, each mailing a new code")
public record SignUpStarted(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
        implements DomainEvent {

    /** The current payload schema version. */
    public static final int VERSION = 1;
}
