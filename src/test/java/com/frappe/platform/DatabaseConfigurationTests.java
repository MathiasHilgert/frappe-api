package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.FrappeApiApplication;
import org.junit.jupiter.api.Test;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.builder.SpringApplicationBuilder;

class DatabaseConfigurationTests {

    @Test
    void startupOutsideLocalProfileFailsWithoutDatabaseCredentials() {
        var app = new SpringApplicationBuilder(FrappeApiApplication.class)
                .web(WebApplicationType.NONE)
                .properties("spring.docker.compose.enabled=false");

        assertThatThrownBy(() -> app.run())
                .hasStackTraceContaining("Missing database setting")
                .hasStackTraceContaining("FRAPPE_DB_URL")
                .hasStackTraceContaining("MissingDatabaseSettingsException");
    }
}
