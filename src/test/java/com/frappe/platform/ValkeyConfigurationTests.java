package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.FrappeApiApplication;
import org.junit.jupiter.api.Test;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.builder.SpringApplicationBuilder;

class ValkeyConfigurationTests {

    @Test
    void startupOutsideLocalProfileFailsWithoutTheValkeyUrl() {
        // Given the database settings, but no Valkey URL
        var app = new SpringApplicationBuilder(FrappeApiApplication.class)
                .web(WebApplicationType.NONE)
                .properties(
                        "spring.docker.compose.enabled=false",
                        "FRAPPE_DB_URL=jdbc:postgresql://localhost:1/frappe",
                        "FRAPPE_APP_PASSWORD=unused",
                        "FRAPPE_OWNER_PASSWORD=unused");

        // Then
        assertThatThrownBy(() -> app.run())
                .hasStackTraceContaining("MissingValkeySettingsException")
                .hasStackTraceContaining("FRAPPE_VALKEY_URL");
    }
}
