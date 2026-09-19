package com.frappe.probe;

import com.frappe.platform.DomainEvent;
import java.time.Instant;
import java.util.UUID;
import org.springframework.modulith.events.Externalized;

/** Published by the probe module's commands; externalized, so it lands in the outbox. */
@Externalized
public record ProbeRecorded(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
        implements DomainEvent {}
