package com.frappe;

import io.opentelemetry.sdk.testing.exporter.InMemorySpanExporter;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;

/**
 * Collects finished spans in memory. Use it with {@code @AutoConfigureTracing} and {@code
 * management.opentelemetry.tracing.export.schedule-delay=50ms}; reset the exporter before each test, because cached
 * contexts keep the spans of earlier tests.
 */
@TestConfiguration(proxyBeanMethods = false)
public class TestTracingConfiguration {

    @Bean
    InMemorySpanExporter inMemorySpanExporter() {
        return InMemorySpanExporter.create();
    }
}
