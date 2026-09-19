package com.frappe.identity;

import com.frappe.platform.Counted;
import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.UUID;

/**
 * A person registered: their address was proven and their account is active. Public, for the modules that act on new
 * people. It carries the person id only ({@link #aggregateId()}); names and addresses are read live through identity.
 *
 * @param eventId the event's id
 * @param occurredAt when the person registered
 * @param aggregateId the person's id
 * @param aggregateVersion the person's version this event produced
 * @param eventVersion the payload's schema version
 */
@Counted(name = "people.registered", description = "People who completed a sign-up")
public record PersonRegistered(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
        implements DomainEvent {

    /** The current payload schema version. */
    public static final int VERSION = 1;
}
