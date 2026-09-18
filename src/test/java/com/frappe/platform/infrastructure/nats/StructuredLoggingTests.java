package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;

import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.LoggerContext;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Properties;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.springframework.boot.logging.logback.StructuredLogEncoder;
import org.springframework.core.env.Environment;
import org.springframework.core.env.StandardEnvironment;
import org.springframework.core.io.ClassPathResource;
import org.springframework.core.io.support.PropertiesLoaderUtils;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.json.JsonMapper;

/**
 * Checks the configured console format and what it makes of our log events, without re-initializing the JVM-wide
 * logging system (cached Spring test contexts share it).
 */
class StructuredLoggingTests {

    private static final String FORMAT_PROPERTY = "logging.structured.format.console";
    private static final String UNREACHABLE_URL = "nats://localhost:1";

    @Test
    void usesEcsInTheBaseConfigAndPlainTextLocally() throws IOException {
        assertThat(load("application.properties").getProperty(FORMAT_PROPERTY)).isEqualTo("ecs");
        assertThat(load("application-local.properties").getProperty(FORMAT_PROPERTY))
                .isEmpty();
    }

    @Test
    void writesTheNatsUrlOfTheUnavailableWarningAsAnEcsField() throws IOException {
        // Given our NATS client logger is captured
        var logger = (Logger) LoggerFactory.getLogger(NatsClient.class);
        var captured = new ListAppender<ILoggingEvent>();
        captured.start();
        logger.addAppender(captured);
        var client = new NatsClient(
                new NatsProperties(
                        UNREACHABLE_URL, "test", Duration.ofMillis(100), Duration.ofSeconds(1), Duration.ofSeconds(1)),
                connection -> {});

        // When the client starts without a reachable NATS
        try {
            client.start();
        } finally {
            client.close();
            logger.detachAppender(captured);
        }

        // Then the base format renders the warning as ECS JSON with the URL as a field
        var warning = captured.list.stream()
                .filter(event -> event.getFormattedMessage().startsWith("NATS is unavailable"))
                .findFirst()
                .orElseThrow();
        var json = ecs(load("application.properties").getProperty(FORMAT_PROPERTY), warning);
        assertThat(json.path("log").path("level").asString()).isEqualTo("WARN");
        assertThat(json.path("natsUrl").asString()).isEqualTo(UNREACHABLE_URL);
        assertThat(json.path("ecs").path("version").isMissingNode()).isFalse();
    }

    private static JsonNode ecs(String format, ILoggingEvent event) {
        var context = new LoggerContext();
        context.putObject(Environment.class.getName(), new StandardEnvironment());
        var encoder = new StructuredLogEncoder();
        encoder.setContext(context);
        encoder.setFormat(format);
        encoder.start();
        var line = new String(encoder.encode(event), StandardCharsets.UTF_8);
        return JsonMapper.builder().build().readTree(line);
    }

    private static Properties load(String resource) throws IOException {
        return PropertiesLoaderUtils.loadProperties(new ClassPathResource(resource));
    }
}
