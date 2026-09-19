package com.frappe.platform.infrastructure.digests;

/** The digest pepper is absent or too short; startup stops instead of digesting with a weak key. */
final class MissingDigestSettingsException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description naming the environment variable, never its value
     */
    MissingDigestSettingsException(String message) {
        super(message);
    }
}
