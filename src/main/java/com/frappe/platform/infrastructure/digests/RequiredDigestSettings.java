package com.frappe.platform.infrastructure.digests;

import org.springframework.boot.EnvironmentPostProcessor;
import org.springframework.boot.SpringApplication;
import org.springframework.core.Ordered;
import org.springframework.core.env.ConfigurableEnvironment;

/**
 * Fails startup early when the digest pepper is missing or shorter than {@value HmacKeyedDigests#MIN_KEY_LENGTH}
 * characters, or equal to the secret pepper (their rotations differ, see {@code docs/secrets.md}). The Binder would otherwise pass an unresolved placeholder through as the key.
 */
class RequiredDigestSettings implements EnvironmentPostProcessor, Ordered {

    private static final String PROPERTY = "frappe.digests.pepper";

    private static final String SECRET_PEPPER = "frappe.secrets.pepper";

    /** Creates the post-processor; instantiated by Spring Boot from {@code spring.factories}. */
    RequiredDigestSettings() {}

    @Override
    public void postProcessEnvironment(ConfigurableEnvironment environment, SpringApplication application) {
        var pepper = resolve(environment, PROPERTY);
        if (pepper == null || pepper.length() < HmacKeyedDigests.MIN_KEY_LENGTH) {
            throw new MissingDigestSettingsException("Missing or too short digest setting FRAPPE_DIGEST_PEPPER. Set it"
                    + " as an environment variable of at least " + HmacKeyedDigests.MIN_KEY_LENGTH
                    + " random characters (e.g. 'openssl rand -base64 48'), different from FRAPPE_SECRET_PEPPER, or"
                    + " run with the 'local' profile (./gradlew bootRun activates it) for its non-secret default.");
        }
        if (pepper.equals(resolve(environment, SECRET_PEPPER))) {
            throw new MissingDigestSettingsException("FRAPPE_DIGEST_PEPPER must differ from FRAPPE_SECRET_PEPPER: the"
                    + " code pepper rotates routinely, the digest key only after a leak. Generate a separate value,"
                    + " e.g. 'openssl rand -base64 48'.");
        }
    }

    private static String resolve(ConfigurableEnvironment environment, String property) {
        try {
            return environment.getProperty(property);
        } catch (IllegalArgumentException unresolvedPlaceholder) {
            return null;
        }
    }

    @Override
    public int getOrder() {
        return Ordered.LOWEST_PRECEDENCE;
    }
}
