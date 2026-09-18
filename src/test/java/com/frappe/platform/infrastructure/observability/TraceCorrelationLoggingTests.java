package com.frappe.platform.infrastructure.observability;

import static org.assertj.core.api.Assertions.assertThat;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.LoggerContext;
import ch.qos.logback.classic.spi.LoggingEvent;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.boot.logging.logback.StructuredLogEncoder;
import org.springframework.core.env.Environment;
import org.springframework.core.env.PropertiesPropertySource;
import org.springframework.core.env.StandardEnvironment;
import org.springframework.core.io.ClassPathResource;
import org.springframework.core.io.support.PropertiesLoaderUtils;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.json.JsonMapper;

/** Renders a log event carrying Micrometer Tracing's MDC entries with the configured ECS format. */
class TraceCorrelationLoggingTests {

    private static final String TRACE_ID = "4bf92f3577b34da6a3ce929d0e0e4736";
    private static final String SPAN_ID = "00f067aa0ba902b7";

    @Test
    void writesTheTraceAndSpanIdsAsEcsFields() throws IOException {
        // Given an event logged inside a span (Micrometer Tracing puts the ids in the MDC)
        var event = new LoggingEvent();
        event.setLevel(Level.INFO);
        event.setLoggerName("com.frappe.Probe");
        event.setMessage("Handled");
        event.setMDCPropertyMap(Map.of("traceId", TRACE_ID, "spanId", SPAN_ID));

        // When
        var json = renderWithBaseConfiguration(event);

        // Then the ids use the ECS names, written dotted like the ecs-logging libraries do
        assertThat(json.path("trace.id").asString()).isEqualTo(TRACE_ID);
        assertThat(json.path("span.id").asString()).isEqualTo(SPAN_ID);
        assertThat(json.has("traceId")).isFalse();
        assertThat(json.has("spanId")).isFalse();
    }

    private static JsonNode renderWithBaseConfiguration(LoggingEvent event) throws IOException {
        var properties = PropertiesLoaderUtils.loadProperties(new ClassPathResource("application.properties"));
        var environment = new StandardEnvironment();
        environment.getPropertySources().addFirst(new PropertiesPropertySource("application", properties));
        var context = new LoggerContext();
        context.putObject(Environment.class.getName(), environment);
        event.setLoggerContext(context);
        var encoder = new StructuredLogEncoder();
        encoder.setContext(context);
        encoder.setFormat(properties.getProperty("logging.structured.format.console"));
        encoder.start();
        var line = new String(encoder.encode(event), StandardCharsets.UTF_8);
        return JsonMapper.builder().build().readTree(line);
    }
}
