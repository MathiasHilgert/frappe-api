package com.frappe.platform.infrastructure.observability;

import io.opentelemetry.api.OpenTelemetry;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Wires the OpenTelemetry pieces Spring Boot leaves to the application. */
@Configuration(proxyBeanMethods = false)
class ObservabilityConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    ObservabilityConfiguration() {}

    /**
     * Exports application logs over OTLP next to the console output.
     *
     * @param openTelemetry Boot's OpenTelemetry SDK
     * @return the bridge
     */
    @Bean
    OtlpLogBridge otlpLogBridge(OpenTelemetry openTelemetry) {
        return new OtlpLogBridge(openTelemetry);
    }
}
