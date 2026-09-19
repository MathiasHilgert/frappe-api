package com.frappe.platform.mail;

/**
 * The mail provider refused a message or could not be reached. Thrown by {@link Mailer#send}, so the calling listener
 * fails and the outbox retries the publication. The message names the provider and the failure kind only: never the
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
