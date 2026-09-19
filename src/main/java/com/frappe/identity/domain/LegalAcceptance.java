package com.frappe.identity.domain;

import java.time.Instant;
import java.util.Objects;

/**
 * What a person accepted and when: the exact versions, so consent is provable per version.
 *
 * @param termsVersion the accepted terms version
 * @param privacyVersion the accepted privacy policy version
 * @param acceptedAt when they were accepted
 */
public record LegalAcceptance(String termsVersion, String privacyVersion, Instant acceptedAt) {

    /**
     * Validates the components.
     *
     * @param termsVersion the accepted terms version, not {@code null}
     * @param privacyVersion the accepted privacy policy version, not {@code null}
     * @param acceptedAt when they were accepted, not {@code null}
     */
    public LegalAcceptance {
        Objects.requireNonNull(termsVersion, "termsVersion");
        Objects.requireNonNull(privacyVersion, "privacyVersion");
        Objects.requireNonNull(acceptedAt, "acceptedAt");
    }
}
