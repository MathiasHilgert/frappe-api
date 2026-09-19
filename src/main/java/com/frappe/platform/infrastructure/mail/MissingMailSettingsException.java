package com.frappe.platform.infrastructure.mail;

/**
 * The sender address is absent or invalid, or the Resend API key is absent; startup stops instead of failing on the
 * first mail.
 */
final class MissingMailSettingsException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception.
     *
     * @param message actionable description naming the missing environment variables
     */
    MissingMailSettingsException(String message) {
        super(message);
    }
}
