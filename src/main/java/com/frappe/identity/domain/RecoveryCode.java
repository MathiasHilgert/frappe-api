package com.frappe.identity.domain;

import java.util.Objects;
import java.util.UUID;

/**
 * One stored recovery code of a person: only its keyed digest, never the code.
 *
 * @param id its id
 * @param digest the keyed digest of the person id and the code ({@link RecoveryCodes#digestOf})
 */
public record RecoveryCode(UUID id, String digest) {

    /**
     * Validates the components.
     *
     * @param id its id, not {@code null}
     * @param digest the keyed digest, not {@code null}
     */
    public RecoveryCode {
        Objects.requireNonNull(id, "id");
        Objects.requireNonNull(digest, "digest");
    }

    @Override
    public String toString() {
        return "RecoveryCode[id=" + id + "]";
    }
}
