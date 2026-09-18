package com.frappe.platform.infrastructure.nats;

/** Publishing an event to JetStream failed; the publication stays incomplete and is retried. */
final class EventPublicationException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description
     * @param cause the underlying failure
     */
    EventPublicationException(String message, Throwable cause) {
        super(message, cause);
    }
}
