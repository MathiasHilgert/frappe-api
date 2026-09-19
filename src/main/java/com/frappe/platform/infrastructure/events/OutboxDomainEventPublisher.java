package com.frappe.platform.infrastructure.events;

import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.infrastructure.tracing.EventTraceContexts;
import java.util.List;
import java.util.Objects;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.modulith.events.EventExternalizationConfiguration;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/**
 * Publishes through Spring's {@link ApplicationEventPublisher}; Spring Modulith's publication registry intercepts the
 * event and writes one outbox row per interested listener in the caller's transaction. An event that leaves the
 * process is stored with the trace context it was recorded in, in the same transaction.
 */
// MANDATORY on every method: outside a transaction Modulith's transactional listeners never run, so the event would
// be dropped silently instead of reaching the outbox.
@Component
@Transactional(propagation = Propagation.MANDATORY)
class OutboxDomainEventPublisher implements DomainEventPublisher {

    private final ApplicationEventPublisher applicationEvents;
    private final EventExternalizationConfiguration externalization;
    private final EventTraceContexts traceContexts;

    /**
     * Creates the publisher.
     *
     * @param applicationEvents Spring's publisher, backed by Modulith's persistent multicaster
     * @param externalization selects the events that leave the process
     * @param traceContexts stores the creation context of those events
     */
    OutboxDomainEventPublisher(
            ApplicationEventPublisher applicationEvents,
            EventExternalizationConfiguration externalization,
            EventTraceContexts traceContexts) {
        this.applicationEvents = applicationEvents;
        this.externalization = externalization;
        this.traceContexts = traceContexts;
    }

    @Override
    public void publish(DomainEvent event) {
        Objects.requireNonNull(event, "event must not be null");
        // Only externalized events are read back by a transport; storing the context of the others would be waste.
        if (externalization.supports(event)) {
            traceContexts.recordCurrent(event.eventId());
        }
        applicationEvents.publishEvent(event);
    }

    @Override
    public void publishAll(List<? extends DomainEvent> events) {
        events.forEach(this::publish);
    }
}
