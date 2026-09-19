package com.frappe.platform.infrastructure.observability;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.infrastructure.web.ProbeRoutes;
import io.opentelemetry.api.trace.SpanKind;
import io.opentelemetry.sdk.testing.exporter.InMemorySpanExporter;
import io.opentelemetry.sdk.trace.data.SpanData;
import java.time.Duration;
import java.util.List;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.ApplicationContext;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.client.RestTestClient;
import org.springframework.util.ClassUtils;

@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = "management.opentelemetry.tracing.export.schedule-delay=50ms")
@AutoConfigureTracing
@AutoConfigureRestTestClient
@Import({
    TestcontainersConfiguration.class,
    TestNatsConfiguration.class,
    RequestTracingTests.Probe.class,
    ProbeRoutes.class
})
@ActiveProfiles("local")
class RequestTracingTests {

    static final String PROBE_PATH = ProbeRoutes.PROBE_PATH;
    static final String MODULE_OBSERVABILITY =
            "org.springframework.modulith.observability.support.ModuleObservabilityBeanPostProcessor";

    @TestConfiguration(proxyBeanMethods = false)
    static class Probe {

        @Bean
        InMemorySpanExporter inMemorySpanExporter() {
            return InMemorySpanExporter.create();
        }
    }

    @Autowired
    RestTestClient http;

    @Autowired
    InMemorySpanExporter spans;

    @Autowired
    ApplicationContext context;

    @BeforeEach
    void forgetEarlierSpans() {
        spans.reset();
    }

    @Test
    void aRequestProducesAnHttpServerSpanWithDatabaseSpansInTheSameTrace() {
        // When
        http.get().uri("/v1" + PROBE_PATH).exchange().expectStatus().isOk();

        // Then
        var server = await().atMost(Duration.ofSeconds(10)).until(this::probeServerSpan, span -> span != null);
        await().atMost(Duration.ofSeconds(10))
                .untilAsserted(() -> assertThat(spansOf(server.getTraceId()))
                        .extracting(SpanData::getName)
                        .contains("query"));
    }

    @Test
    void observesApplicationModuleEntriesAndListeners() {
        // Then Spring Modulith wraps module APIs and cross-module event listeners in observations
        // (the processor lives in the runtime-only spring-modulith-observability-core, hence the lookup by name)
        var processor =
                ClassUtils.resolveClassName(MODULE_OBSERVABILITY, getClass().getClassLoader());
        assertThat(context.getBeanNamesForType(processor)).hasSize(1);
    }

    @Test
    void logLinesOfARequestCarryItsTraceAndSpanIds() {
        // Given the probe's log events are captured
        var logger = (Logger) LoggerFactory.getLogger(ProbeRoutes.ProbeController.class);
        var captured = new ListAppender<ILoggingEvent>();
        captured.start();
        logger.addAppender(captured);

        // When
        try {
            http.get().uri("/v1" + PROBE_PATH).exchange().expectStatus().isOk();
        } finally {
            logger.detachAppender(captured);
        }

        // Then
        var server = await().atMost(Duration.ofSeconds(10)).until(this::probeServerSpan, span -> span != null);
        var json = EcsLogRenderer.render(captured.list.getFirst());
        assertThat(captured.list).hasSize(1);
        assertThat(json.path("trace.id").asString()).isEqualTo(server.getTraceId());
        // The controller runs inside Spring Security's secured-request span, a child of the server span in its trace
        await().atMost(Duration.ofSeconds(10))
                .untilAsserted(() -> assertThat(spansOf(server.getTraceId()))
                        .extracting(SpanData::getSpanId)
                        .contains(json.path("span.id").asString()));
    }

    private SpanData probeServerSpan() {
        return spans.getFinishedSpanItems().stream()
                .filter(span -> span.getKind() == SpanKind.SERVER)
                .filter(span -> span.getName().contains(PROBE_PATH))
                .findFirst()
                .orElse(null);
    }

    private List<SpanData> spansOf(String traceId) {
        return spans.getFinishedSpanItems().stream()
                .filter(span -> span.getTraceId().equals(traceId))
                .toList();
    }
}
