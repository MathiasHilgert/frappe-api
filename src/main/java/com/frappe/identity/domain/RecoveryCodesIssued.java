package com.frappe.identity.domain;

import com.frappe.platform.Counted;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.MetricTag;
import java.time.Instant;
import java.util.UUID;

/**
 * A person was handed a fresh set of recovery codes. Internal to identity and counted only; it carries the person id
 * ({@link #aggregateId()}) and why, never a code.
 *
 * @param eventId the event's id
 * @param occurredAt when the codes were issued
 * @param aggregateId the person's id
 * @param aggregateVersion the person's version this event produced
 * @param eventVersion the payload's schema version
 * @param reason why the codes were issued
 */
@Counted(
        name = "recovery_codes.issued",
        description = "Sets of recovery codes handed to a person",
        tags = @MetricTag(key = "reason", from = "reason"))
public record RecoveryCodesIssued(
        UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion, Reason reason)
        implements DomainEvent {

    /** The current payload schema version. */
    public static final int VERSION = 1;

    /** Why a person got recovery codes. */
    public enum Reason {
        /** With the account, at sign-up. */
        SIGN_UP,
        /** Replacing the earlier set, on request. */
        REGENERATED
    }
}
