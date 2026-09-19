package com.frappe.identity.domain;

import com.frappe.identity.domain.PasswordRejected.Reason;
import com.frappe.platform.Result;
import java.text.Normalizer;
import java.util.Objects;

/**
 * A password that meets the length rule of NIST SP 800-63B-4: NFKC-normalized, then {@value #MIN_LENGTH} to
 * {@value #MAX_LENGTH} code points, with no composition rules. Normalizing first makes a password typed in fullwidth
 * letters, or with ligatures, the same password as its plain twin on every keyboard. It never prints its value.
 *
 * <p>Length alone does not make a password acceptable; {@link PasswordPolicy} adds the breach check.
 *
 * @param value the normalized password
 */
public record Password(String value) {

    /** Fewest code points a password may have. */
    public static final int MIN_LENGTH = 15;

    /** Most code points a password may have; bounds the hashing work one request can cause. */
    public static final int MAX_LENGTH = 128;

    /**
     * Validates the value.
     *
     * @param value the normalized password, not {@code null}
     */
    public Password {
        Objects.requireNonNull(value, "value");
    }

    /**
     * Normalizes a password as typed and checks its length.
     *
     * @param raw the password as typed, not {@code null}
     * @return the password, or {@link Reason#TOO_SHORT} / {@link Reason#TOO_LONG}
     */
    public static Result<Password, PasswordRejected> of(String raw) {
        var normalized = Normalizer.normalize(raw, Normalizer.Form.NFKC);
        var length = normalized.codePointCount(0, normalized.length());
        if (length < MIN_LENGTH) {
            return Result.failure(new PasswordRejected(Reason.TOO_SHORT));
        }
        if (length > MAX_LENGTH) {
            return Result.failure(new PasswordRejected(Reason.TOO_LONG));
        }
        return Result.success(new Password(normalized));
    }

    @Override
    public String toString() {
        return "Password[<redacted>]";
    }
}
