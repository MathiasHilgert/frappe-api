package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.FrappeApiApplication;
import org.assertj.core.util.Throwables;
import org.junit.jupiter.api.Test;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.builder.SpringApplicationBuilder;

class DigestConfigurationTests {

    private static final String SHARED_PEPPER = "shared-but-not-a-secret-".repeat(2);

    private static final String SHORT_PEPPER = "short-digest-pepper";

    @Test
    void startupOutsideLocalProfileFailsWithoutTheDigestPepper() {
        // Given every other required setting, but no digest pepper
        var app = appWith("FRAPPE_VALKEY_URL=redis://localhost:1");

        // Then
        assertThatThrownBy(() -> app.run())
                .hasStackTraceContaining("MissingDigestSettingsException")
                .hasStackTraceContaining("FRAPPE_DIGEST_PEPPER");
    }

    @Test
    void startupOutsideLocalProfileFailsWithADigestPepperShorterThan32Characters() {
        // Given a short digest pepper
        var app = appWith("FRAPPE_VALKEY_URL=redis://localhost:1", "FRAPPE_DIGEST_PEPPER=" + SHORT_PEPPER);

        // Then the error names the variable, never its value
        assertThatThrownBy(() -> app.run())
                .hasStackTraceContaining("MissingDigestSettingsException")
                .hasStackTraceContaining("FRAPPE_DIGEST_PEPPER")
                .satisfies(
                        failure -> assertThat(Throwables.getStackTrace(failure)).doesNotContain(SHORT_PEPPER));
    }

    @Test
    void startupOutsideLocalProfileFailsWhenTheDigestPepperIsTheSecretPepper() {
        // Given one key for both peppers
        var app = appWith(
                "FRAPPE_VALKEY_URL=redis://localhost:1",
                "FRAPPE_SECRET_PEPPER=" + SHARED_PEPPER,
                "FRAPPE_DIGEST_PEPPER=" + SHARED_PEPPER);

        // Then the error names both variables, never the value
        assertThatThrownBy(() -> app.run())
                .hasStackTraceContaining("MissingDigestSettingsException")
                .hasStackTraceContaining("FRAPPE_DIGEST_PEPPER")
                .hasStackTraceContaining("FRAPPE_SECRET_PEPPER")
                .satisfies(
                        failure -> assertThat(Throwables.getStackTrace(failure)).doesNotContain(SHARED_PEPPER));
    }

    private static SpringApplicationBuilder appWith(String... properties) {
        return new SpringApplicationBuilder(FrappeApiApplication.class)
                .web(WebApplicationType.NONE)
                .properties(
                        "spring.docker.compose.enabled=false",
                        "FRAPPE_DB_URL=jdbc:postgresql://localhost:1/frappe",
                        "FRAPPE_APP_PASSWORD=unused",
                        "FRAPPE_OWNER_PASSWORD=unused",
                        "FRAPPE_SECRET_PEPPER=unused-but-present-0123456789abcdef",
                        "RESEND_API_KEY=unused",
                        "FRAPPE_MAIL_FROM=Frappe <no-reply@example.com>")
                .properties(properties);
    }
}
