package com.frappe.identity.domain;

import java.util.Objects;

/**
 * Why the password policy refused a password.
 *
 * @param reason the rule the password broke
 */
public record PasswordRejected(Reason reason) {

    /**
     * Validates the reason.
     *
     * @param reason the rule the password broke, not {@code null}
     */
    public PasswordRejected {
        Objects.requireNonNull(reason, "reason");
    }

    /** The rules of the password policy. */
    public enum Reason {
        /** Fewer than {@value Password#MIN_LENGTH} code points after normalization. */
        TOO_SHORT,
        /** More than {@value Password#MAX_LENGTH} code points after normalization. */
        TOO_LONG,
        /** The password appears in a public breach corpus. */
        BREACHED
    }
}
