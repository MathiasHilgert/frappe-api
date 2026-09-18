package com.frappe.platform.infrastructure.observability;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import io.opentelemetry.sdk.trace.export.SpanExporter;
import java.time.Duration;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.micrometer.metrics.test.autoconfigure.AutoConfigureMetrics;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.ApplicationContext;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.client.RestTestClient;
import org.springframework.util.ClassUtils;

/** Telemetry must never reduce availability: with no OTLP receiver, requests still succeed and export fails quietly. */
@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = {
            "management.opentelemetry.tracing.export.otlp.endpoint=http://localhost:1/v1/traces",
            "management.opentelemetry.tracing.export.schedule-delay=50ms",
            "management.otlp.metrics.export.url=http://localhost:1/v1/metrics",
            "management.otlp.metrics.export.step=100ms"
        })
@AutoConfigureTracing
@AutoConfigureMetrics
@AutoConfigureRestTestClient
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class OtlpUnavailableTests {

    private static final String HEALTH = "/actuator/health";
    private static final String OTLP_METER_REGISTRY = "io.micrometer.registry.otlp.OtlpMeterRegistry";

    @Autowired
    RestTestClient http;

    @Autowired
    ApplicationContext context;

    @Test
    void keepsServingWhileTheOtlpEndpointIsUnreachable() {
        // Given both OTLP exporters are active (the metrics registry is a runtime-only dependency, hence by name)
        var otlpMetrics =
                ClassUtils.resolveClassName(OTLP_METER_REGISTRY, getClass().getClassLoader());
        assertThat(context.getBeanNamesForType(otlpMetrics)).hasSize(1);
        assertThat(context.getBeanNamesForType(SpanExporter.class)).isNotEmpty();

        // And traffic that produces spans and metrics for the unreachable receiver
        IntStream.range(0, 5)
                .forEach(i -> http.get().uri(HEALTH).exchange().expectStatus().isOk());

        // When several export rounds have failed
        await().pollDelay(Duration.ofMillis(500)).atMost(Duration.ofSeconds(2)).until(() -> true);

        // Then callers still get fast, successful responses
        await().atMost(Duration.ofSeconds(1))
                .untilAsserted(
                        () -> http.get().uri(HEALTH).exchange().expectStatus().isOk());
    }
}
