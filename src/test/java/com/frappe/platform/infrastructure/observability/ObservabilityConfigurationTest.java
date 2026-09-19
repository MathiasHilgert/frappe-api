package com.frappe.platform.infrastructure.observability;

import static org.assertj.core.api.Assertions.assertThat;

import java.io.IOException;
import org.junit.jupiter.api.Test;
import org.springframework.core.io.ClassPathResource;
import org.springframework.core.io.support.PropertiesLoaderUtils;

/** Privacy-relevant telemetry settings are explicit in the base configuration, not left to library defaults. */
class ObservabilityConfigurationTest {

    @Test
    void neverRecordsSqlParameterValuesOnSpans() throws IOException {
        // When
        var properties = PropertiesLoaderUtils.loadProperties(new ClassPathResource("application.properties"));

        // Then
        assertThat(properties.getProperty("jdbc.datasource-proxy.include-parameter-values"))
                .isEqualTo("false");
    }
}
