package com.frappe.identity.domain;

import com.frappe.platform.Result;
import java.text.Normalizer;
import java.util.Objects;

/**
 * A given or family name: trimmed, NFC-normalized, 1 to {@value #MAX_LENGTH} characters (code points).
 *
 * @param value the normalized name
 */
public record PersonName(String value) {

    /** Most characters a name may have. */
    public static final int MAX_LENGTH = 80;

    /**
     * Validates the value.
     *
     * @param value the normalized name, not {@code null}
     */
    public PersonName {
        Objects.requireNonNull(value, "value");
    }

    /**
     * Normalizes and checks a name as entered.
     *
     * @param raw the name as entered, not {@code null}
     * @return the name, or why it was refused
     */
    public static Result<PersonName, NameRejected> of(String raw) {
        var normalized = Normalizer.normalize(raw.strip(), Normalizer.Form.NFC);
        if (normalized.isEmpty()) {
            return Result.failure(NameRejected.EMPTY);
        }
        if (normalized.codePointCount(0, normalized.length()) > MAX_LENGTH) {
            return Result.failure(NameRejected.TOO_LONG);
        }
        return Result.success(new PersonName(normalized));
    }
}
