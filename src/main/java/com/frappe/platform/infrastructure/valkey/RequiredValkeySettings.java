package com.frappe.platform.infrastructure.valkey;

import java.util.Map;
import org.springframework.boot.EnvironmentPostProcessor;
import org.springframework.boot.SpringApplication;
import org.springframework.core.Ordered;
import org.springframework.core.env.ConfigurableEnvironment;

/**
 * Fails startup early when the Valkey URL or the secret pepper is missing. The Binder would otherwise pass an unresolved
 * placeholder through as the value and fail later with a misleading error.
 */
class RequiredValkeySettings implements EnvironmentPostProcessor, Ordered {

    private static final Map<String, String> REQUIRED = Map.of(
            "spring.data.redis.url", "FRAPPE_VALKEY_URL",
            "frappe.secrets.pepper", "FRAPPE_SECRET_PEPPER");

    /** Creates the post-processor; instantiated by Spring Boot from {@code spring.factories}. */
    RequiredValkeySettings() {}

    @Override
    public void postProcessEnvironment(ConfigurableEnvironment environment, SpringApplication application) {
        var missing = REQUIRED.entrySet().stream()
                .filter(setting -> !resolves(environment, setting.getKey()))
                .map(Map.Entry::getValue)
                .sorted()
                .toList();
        if (!missing.isEmpty()) {
            throw new MissingValkeySettingsException("Missing Valkey setting(s) " + String.join(", ", missing)
                    + ". Set them as environment variables (FRAPPE_VALKEY_URL: redis://host:6379, rediss:// for TLS;"
                    + " FRAPPE_SECRET_PEPPER: at least 32 random characters, e.g. 'openssl rand -base64 48'), or run"
                    + " with the 'local' profile (./gradlew bootRun activates it) for the compose.yaml defaults.");
        }
    }

    private static boolean resolves(ConfigurableEnvironment environment, String key) {
        try {
            var value = environment.getProperty(key);
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
