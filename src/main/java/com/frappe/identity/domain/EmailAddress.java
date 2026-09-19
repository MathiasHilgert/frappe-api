package com.frappe.identity.domain;

import com.frappe.platform.Result;
import java.util.Locale;
import java.util.Objects;

/**
 * An email address as the person entered it, trimmed: at most {@value #MAX_LENGTH} characters (RFC 5321's path
 * limit) with exactly one {@code @}. Deliverability is proven by a code, not by syntax, so nothing more is checked.
 * {@link #canonical()} is the form used for uniqueness and digests. It prints masked.
 *
 * @param value the trimmed address, as entered
 */
public record EmailAddress(String value) {

    /** Most characters an address may have. */
    public static final int MAX_LENGTH = 254;

    /**
     * Validates the value.
     *
     * @param value the trimmed address, not {@code null}
     */
    public EmailAddress {
        Objects.requireNonNull(value, "value");
    }

    /**
     * Trims and checks an address as entered.
     *
     * @param raw the address as entered, not {@code null}
     * @return the address, or why it was refused
     */
    public static Result<EmailAddress, EmailAddressRejected> of(String raw) {
        var trimmed = raw.strip();
        if (trimmed.length() > MAX_LENGTH) {
            return Result.failure(EmailAddressRejected.TOO_LONG);
        }
        var at = trimmed.indexOf('@');
        if (at <= 0 || at != trimmed.lastIndexOf('@') || at == trimmed.length() - 1) {
            return Result.failure(EmailAddressRejected.MALFORMED);
        }
        return Result.success(new EmailAddress(trimmed));
    }

    /**
     * The canonical form: lowercase, independent of the default locale.
     *
     * @return the lowercase address
     */
    public String canonical() {
        return value.toLowerCase(Locale.ROOT);
    }

    @Override
    public String toString() {
        return "EmailAddress[" + value.charAt(0) + "***" + value.substring(value.indexOf('@')) + "]";
    }
}
