package com.frappe;

import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.UUID;

/** Fixture: an event directly under {@code com.frappe}, outside any module package (never annotated, never scanned). */
public record RootLevelEvent(
        UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
        implements DomainEvent {}
