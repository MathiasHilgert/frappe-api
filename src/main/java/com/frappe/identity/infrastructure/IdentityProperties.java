package com.frappe.identity.infrastructure;

import java.net.URI;
import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * Settings of the identity module ({@code frappe.identity.*}).
 *
 * @param breachedPasswords the breach check of the password policy
 * @param legal the current legal versions
 */
@ConfigurationProperties("frappe.identity")
record IdentityProperties(@DefaultValue BreachedPasswordsProperties breachedPasswords, LegalProperties legal) {

    /**
     * The current legal texts ({@code frappe.identity.legal.*}); a person must accept exactly these to register.
     *
     * @param termsVersion the current terms of service version
     * @param privacyVersion the current privacy policy version
     */
    record LegalProperties(String termsVersion, String privacyVersion) {}

    /**
     * The Pwned Passwords range API ({@code frappe.identity.breached-passwords.*}).
     *
     * @param baseUrl where the range API answers; tests point it at a stub
     * @param timeout the bound on connecting and on waiting for the answer; past it the check is unknown and the
     *     password is accepted
     */
    record BreachedPasswordsProperties(
            @DefaultValue("https://api.pwnedpasswords.com") URI baseUrl,
            @DefaultValue("2s") Duration timeout) {}
}
