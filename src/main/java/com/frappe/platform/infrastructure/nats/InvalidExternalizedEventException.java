package com.frappe.platform.infrastructure.nats;

/** An {@code @Externalized} event breaks the NATS contract (no {@code DomainEvent} envelope, or a custom target). */
final class InvalidExternalizedEventException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description naming the event type
     */
    InvalidExternalizedEventException(String message) {
        super(message);
    }
}
