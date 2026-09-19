package com.frappe.identity;

import com.frappe.identity.domain.EmailMask;
import java.time.Duration;
import java.util.Locale;
import java.util.Objects;

/**
 * A one-time code for a person to type, handed in memory from identity to its {@link CodeMailer}. The code exists only
 * on the stack between the two; it never enters an event, a log or this record's {@link #toString()}.
 *
 * @param recipient the address to send to
 * @param locale the language of the mail
 * @param purpose what the code proves
 * @param code the code to show
 * @param validFor how long the code stays valid, for the mail's text
 */
public record CodeMail(String recipient, Locale locale, CodePurpose purpose, String code, Duration validFor) {

    /**
     * Validates the components.
     *
     * @param recipient the address to send to, not {@code null}
     * @param locale the language of the mail, not {@code null}
     * @param purpose what the code proves, not {@code null}
     * @param code the code to show, not {@code null}
     * @param validFor how long the code stays valid, not {@code null}
     */
    public CodeMail {
        Objects.requireNonNull(recipient, "recipient");
        Objects.requireNonNull(locale, "locale");
        Objects.requireNonNull(purpose, "purpose");
        Objects.requireNonNull(code, "code");
        Objects.requireNonNull(validFor, "validFor");
    }

    @Override
    public String toString() {
        return "CodeMail[recipient=%s, locale=%s, purpose=%s, validFor=%s]"
                .formatted(EmailMask.of(recipient), locale, purpose, validFor);
    }
}
