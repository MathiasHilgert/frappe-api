package com.frappe.platform.infrastructure.persistence;

import java.util.Map;
import org.springframework.boot.EnvironmentPostProcessor;
import org.springframework.boot.SpringApplication;
import org.springframework.core.Ordered;
import org.springframework.core.env.ConfigurableEnvironment;

/**
 * Fails startup early when database credentials are missing. The Binder would otherwise pass an unresolved
 * placeholder through as a literal password and fail later with a misleading authentication error.
 */
class RequiredDatabaseSettings implements EnvironmentPostProcessor, Ordered {

    private static final Map<String, String> REQUIRED = Map.of(
            "spring.datasource.url", "FRAPPE_DB_URL",
            "spring.datasource.password", "FRAPPE_APP_PASSWORD",
            "spring.flyway.password", "FRAPPE_OWNER_PASSWORD");

    @Override
    public void postProcessEnvironment(ConfigurableEnvironment environment, SpringApplication application) {
        var missing = REQUIRED.entrySet().stream()
                .filter(setting -> !resolves(environment, setting.getKey()))
                .map(Map.Entry::getValue)
                .sorted()
                .toList();
        if (!missing.isEmpty()) {
            throw new MissingDatabaseSettingsException("Missing database setting(s) " + String.join(", ", missing)
                    + ". Set them as environment variables, or run with the 'local' profile"
                    + " (./gradlew bootRun activates it) for the compose.yaml defaults.");
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
