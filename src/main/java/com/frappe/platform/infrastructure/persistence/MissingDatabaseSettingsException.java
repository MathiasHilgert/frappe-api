package com.frappe.platform.infrastructure.persistence;

/** Required database settings are absent; startup stops before connecting with wrong credentials. */
final class MissingDatabaseSettingsException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description naming the missing environment variables
     */
    MissingDatabaseSettingsException(String message) {
        super(message);
    }
}
