package com.frappe.platform.mail;

/**
 * Delivering a mail failed for a reason a retry can fix: the provider is down or rate-limits, or a setting (sender,
 * API key) must be corrected. Thrown by {@link Mailer#send}, so the calling listener fails and the outbox retries the
 * publication. Permanent rejections (an invalid or refused recipient) are not thrown: the platform logs them once,
 * counts them and does not retry them. The message names the provider and the failure kind only: never the
 * recipient, the body or credentials.
 */
public final class MailDeliveryException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception without a cause, for provider errors whose own messages may echo the recipient.
     *
     * @param message what failed, without personal data or secrets
     */
    public MailDeliveryException(String message) {
        super(message);
    }

    /**
     * Creates the exception.
     *
     * @param message what failed, without personal data or secrets
     * @param cause the transport failure, whose message carries no personal data
     */
    public MailDeliveryException(String message, Throwable cause) {
        super(message, cause);
    }
}
