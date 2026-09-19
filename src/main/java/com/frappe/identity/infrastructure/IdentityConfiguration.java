package com.frappe.identity.infrastructure;

import com.frappe.identity.domain.BreachedPasswords;
import com.frappe.identity.domain.PasswordHasher;
import com.frappe.identity.domain.PasswordPolicy;
import com.frappe.identity.domain.Secrets;
import io.micrometer.observation.ObservationRegistry;
import java.security.SecureRandom;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

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
     * @param observations the registry of the HTTP client observation
     * @return the breach corpus
     */
    @Bean
    BreachedPasswords breachedPasswords(IdentityProperties properties, ObservationRegistry observations) {
        return new HaveIBeenPwnedRestApiPasswordChecker(properties.breachedPasswords(), observations);
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
