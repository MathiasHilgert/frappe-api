package com.frappe.platform.infrastructure.mail;

/**
 * The provider refused a mail for good (an invalid or unknown recipient, a request it will never accept): retrying the
 * same mail cannot succeed. Thrown by a {@link MailTransport} and handled by {@link ObservedMailer}, which logs and counts
 * it and lets the listener complete, so the outbox does not retry it. Never leaves the package.
 */
final class MailRejectedException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception, without a cause: provider replies quote the recipient.
     *
     * @param message the template, the provider and its status, without personal data
     */
    MailRejectedException(String message) {
        super(message);
    }
}
