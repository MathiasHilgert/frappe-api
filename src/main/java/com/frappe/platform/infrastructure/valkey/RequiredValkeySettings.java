package com.frappe.platform.infrastructure.valkey;

import org.springframework.boot.EnvironmentPostProcessor;
import org.springframework.boot.SpringApplication;
import org.springframework.core.Ordered;
import org.springframework.core.env.ConfigurableEnvironment;

/**
 * Fails startup early when the Valkey URL is missing. The Binder would otherwise pass the unresolved placeholder
 * through as the URL and fail later with a misleading URL syntax error.
 */
class RequiredValkeySettings implements EnvironmentPostProcessor, Ordered {

    private static final String URL_PROPERTY = "spring.data.redis.url";

    private static final String URL_VARIABLE = "FRAPPE_VALKEY_URL";

    /** Creates the post-processor; instantiated by Spring Boot from {@code spring.factories}. */
    RequiredValkeySettings() {}

    @Override
    public void postProcessEnvironment(ConfigurableEnvironment environment, SpringApplication application) {
        if (!resolves(environment)) {
            throw new MissingValkeySettingsException("Missing Valkey setting " + URL_VARIABLE
                    + ". Set it as an environment variable (redis://host:6379, rediss:// for TLS), or run with the"
                    + " 'local' profile (./gradlew bootRun activates it) for the compose.yaml Valkey.");
        }
    }

    private static boolean resolves(ConfigurableEnvironment environment) {
        try {
            var value = environment.getProperty(URL_PROPERTY);
            return value != null && !value.isBlank();
        } catch (IllegalArgumentException unresolvedPlaceholder) {
            return false;
        }
    }

    @Override
    public int getOrder() {
        return Ordered.LOWEST_PRECEDENCE;
    }
}
