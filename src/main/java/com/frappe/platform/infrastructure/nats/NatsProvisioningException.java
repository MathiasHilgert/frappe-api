package com.frappe.platform.infrastructure.nats;

/** The FRAPPE stream could not be created or updated. */
final class NatsProvisioningException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description
     * @param cause the underlying failure
     */
    NatsProvisioningException(String message, Throwable cause) {
        super(message, cause);
    }
}
