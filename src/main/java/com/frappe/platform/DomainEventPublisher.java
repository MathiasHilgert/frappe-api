package com.frappe.platform;

import java.util.List;

/**
 * Records domain events in the transactional outbox. Command handlers call it after saving the aggregate, inside the
 * same transaction, with the events the aggregate raised: the events are stored if and only if the transaction
 * commits, and are relayed (to NATS for {@code @Externalized} events) only after the commit.
 *
 * <p>Calling it without an active transaction is a programming error and fails fast, because the events could not
 * reach the outbox atomically with the state change.
 */
public interface DomainEventPublisher {

    /**
     * Records one event in the outbox of the current transaction.
     *
     * @param event the event raised by the aggregate
     * @throws org.springframework.transaction.IllegalTransactionStateException if no transaction is active
     */
    void publish(DomainEvent event);

    /**
     * Records the events in the outbox of the current transaction, in the given order.
     *
     * @param events the events raised by the aggregate, typically pulled from it after saving
     * @throws org.springframework.transaction.IllegalTransactionStateException if no transaction is active
     */
    void publishAll(List<? extends DomainEvent> events);
}
