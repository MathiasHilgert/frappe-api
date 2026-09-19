package com.frappe.identity;

import java.util.Locale;
import java.util.Objects;
import java.util.UUID;

/**
 * The notice sent instead of a sign-up code when the address already has an account: sign in or reset the password.
 * It holds no code, so only the mailbox holder learns the address is registered.
 *
 * @param recipient the address to send to
 * @param locale the language of the mail
 * @param reference a stable id of the attempt (the sign-up event's id), for the mail's idempotency key
 */
public record AccountExistsMail(String recipient, Locale locale, UUID reference) {

    /**
     * Validates the components.
     *
     * @param recipient the address to send to, not {@code null}
     * @param locale the language of the mail, not {@code null}
     * @param reference a stable id of the attempt, not {@code null}
     */
    public AccountExistsMail {
        Objects.requireNonNull(recipient, "recipient");
        Objects.requireNonNull(locale, "locale");
        Objects.requireNonNull(reference, "reference");
    }

    @Override
    public String toString() {
        return "AccountExistsMail[recipient=%s, locale=%s, reference=%s]"
                .formatted(MaskedRecipient.of(recipient), locale, reference);
    }
}
