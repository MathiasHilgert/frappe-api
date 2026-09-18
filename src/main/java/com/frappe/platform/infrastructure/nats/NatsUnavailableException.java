package com.frappe.platform.infrastructure.nats;

/** No NATS connection exists yet; callers fail fast instead of waiting. */
final class NatsUnavailableException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description
     */
    NatsUnavailableException(String message) {
        super(message);
    }

    /**
     * Creates the exception for a connection that failed underneath the caller.
     *
     * @param message actionable description
     * @param cause the client library's failure
     */
    NatsUnavailableException(String message, Throwable cause) {
        super(message, cause);
    }
}
