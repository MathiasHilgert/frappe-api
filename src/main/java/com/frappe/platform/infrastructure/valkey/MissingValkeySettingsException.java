package com.frappe.platform.infrastructure.valkey;

/** The Valkey URL is absent; startup stops instead of connecting to a guessed server. */
final class MissingValkeySettingsException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description naming the missing environment variable
     */
    MissingValkeySettingsException(String message) {
        super(message);
    }
}
