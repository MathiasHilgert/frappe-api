package com.frappe.platform.infrastructure.mail;

import java.util.LinkedHashMap;
import java.util.Map;
import org.springframework.boot.EnvironmentPostProcessor;
import org.springframework.boot.SpringApplication;
import org.springframework.core.Ordered;
import org.springframework.core.env.ConfigurableEnvironment;

/**
 * Fails startup early when the sender address or, with the Resend provider, the Resend API key is missing. The Binder
 * would otherwise pass an unresolved placeholder through as the value and fail on the first mail instead.
 */
class RequiredMailSettings implements EnvironmentPostProcessor, Ordered {

    private static final String PROVIDER = "frappe.mail.provider";

    private static final String RESEND = "resend";

    /** Creates the post-processor; instantiated by Spring Boot from {@code spring.factories}. */
    RequiredMailSettings() {}

    @Override
    public void postProcessEnvironment(ConfigurableEnvironment environment, SpringApplication application) {
        var required = new LinkedHashMap<String, String>();
        required.put("frappe.mail.from", "FRAPPE_MAIL_FROM");
        if (RESEND.equalsIgnoreCase(environment.getProperty(PROVIDER, RESEND))) {
            required.put("frappe.mail.resend.api-key", "RESEND_API_KEY");
        }
        var missing = required.entrySet().stream()
                .filter(setting -> !resolves(environment, setting.getKey()))
                .map(Map.Entry::getValue)
                .sorted()
                .toList();
        if (!missing.isEmpty()) {
            throw new MissingMailSettingsException("Missing mail setting(s) " + String.join(", ", missing)
                    + ". Set them as environment variables (RESEND_API_KEY: the Resend API key, kept in Bitwarden;"
                    + " FRAPPE_MAIL_FROM: the sender, e.g. 'Frappé <no-reply@example.com>' on a domain verified in"
                    + " Resend), or run with the 'local' profile (./gradlew bootRun activates it) to send to compose's"
                    + " Mailpit.");
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
