package com.frappe.identity.domain;

import java.util.Objects;

/**
 * The current versions of the terms of service and the privacy policy, which a person must accept to register.
 *
 * @param terms the current terms version
 * @param privacy the current privacy policy version
 */
public record LegalVersions(String terms, String privacy) {

    /**
     * Validates the versions.
     *
     * @param terms the current terms version, not {@code null}
     * @param privacy the current privacy policy version, not {@code null}
     */
    public LegalVersions {
        Objects.requireNonNull(terms, "terms");
        Objects.requireNonNull(privacy, "privacy");
    }

    /**
     * Whether the accepted versions are exactly the current ones.
     *
     * @param acceptedTerms the terms version the person accepted
     * @param acceptedPrivacy the privacy policy version the person accepted
     * @return whether both are current
     */
    public boolean areCurrent(String acceptedTerms, String acceptedPrivacy) {
        return terms.equals(acceptedTerms) && privacy.equals(acceptedPrivacy);
    }
}
