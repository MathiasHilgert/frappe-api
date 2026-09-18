package com.frappe.platform.infrastructure.events;

import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import java.util.List;
import java.util.Objects;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/**
 * Publishes through Spring's {@link ApplicationEventPublisher}; Spring Modulith's publication registry intercepts the
 * event and writes one outbox row per interested listener in the caller's transaction.
 */
// MANDATORY on every method: outside a transaction Modulith's transactional listeners never run, so the event would
// be dropped silently instead of reaching the outbox.
@Component
@Transactional(propagation = Propagation.MANDATORY)
class OutboxDomainEventPublisher implements DomainEventPublisher {

    private final ApplicationEventPublisher applicationEvents;

    /**
     * Creates the publisher.
     *
     * @param applicationEvents Spring's publisher, backed by Modulith's persistent multicaster
     */
    OutboxDomainEventPublisher(ApplicationEventPublisher applicationEvents) {
        this.applicationEvents = applicationEvents;
    }

    @Override
    public void publish(DomainEvent event) {
        applicationEvents.publishEvent(Objects.requireNonNull(event, "event must not be null"));
    }

    @Override
    public void publishAll(List<? extends DomainEvent> events) {
        events.forEach(this::publish);
    }
}
