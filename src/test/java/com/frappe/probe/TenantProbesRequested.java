package com.frappe.probe;

import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.UUID;

/** Asks the probe module's listener to read the tenant probes of {@code tenantId}; not externalized. */
public record TenantProbesRequested(
        UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion, UUID tenantId)
        implements DomainEvent {}
