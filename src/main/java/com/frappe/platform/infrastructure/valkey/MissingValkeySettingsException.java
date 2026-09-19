package com.frappe.platform.infrastructure.valkey;

/** The Valkey URL or the secret pepper is absent; startup stops instead of guessing either. */
final class MissingValkeySettingsException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description naming the missing environment variables
     */
    MissingValkeySettingsException(String message) {
        super(message);
    }
}
