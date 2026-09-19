package com.frappe.identity.infrastructure;

import com.frappe.identity.domain.BreachedPasswords;
import com.frappe.identity.domain.LegalVersions;
import com.frappe.identity.domain.PasswordHasher;
import com.frappe.identity.domain.PasswordPolicy;
import com.frappe.identity.domain.Secrets;
import java.security.SecureRandom;
import java.util.Objects;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.client.RestClient;

/** Wires identity's domain ports to their adapters, and the one password policy. */
@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(IdentityProperties.class)
class IdentityConfiguration {

    /** Creates the configuration; Spring calls it. */
    IdentityConfiguration() {}

    /**
     * Argon2id hashing.
     *
     * @return the hasher
     */
    @Bean
    PasswordHasher passwordHasher() {
        return new Argon2PasswordHasher();
    }

    /**
     * The Pwned Passwords range API.
     *
     * @param properties identity's settings
     * @param builders Boot's client builder, so its observation, SSL and proxy customizations apply
     * @return the breach corpus
     */
    @Bean
    BreachedPasswords breachedPasswords(IdentityProperties properties, RestClient.Builder builders) {
        return new HaveIBeenPwnedRestApiPasswordChecker(properties.breachedPasswords(), builders);
    }

    /**
     * Secrets from a {@link SecureRandom}.
     *
     * @return the secrets
     */
    @Bean
    Secrets secrets() {
        return new SecureRandomSecrets(new SecureRandom());
    }

    /**
     * The current legal versions; startup fails when either is not set.
     *
     * @param properties identity's settings
     * @return the versions
     */
    @Bean
    LegalVersions legalVersions(IdentityProperties properties) {
        var legal = Objects.requireNonNull(properties.legal(), "frappe.identity.legal.* must be set");
        return new LegalVersions(
                Objects.requireNonNull(legal.termsVersion(), "frappe.identity.legal.terms-version must be set"),
                Objects.requireNonNull(legal.privacyVersion(), "frappe.identity.legal.privacy-version must be set"));
    }

    /**
     * The one password policy.
     *
     * @param breachedPasswords the breach corpus
     * @return the policy
     */
    @Bean
    PasswordPolicy passwordPolicy(BreachedPasswords breachedPasswords) {
        return new PasswordPolicy(breachedPasswords);
    }
}
