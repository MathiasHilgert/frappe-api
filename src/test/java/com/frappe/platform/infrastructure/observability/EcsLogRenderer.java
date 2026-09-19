package com.frappe.platform.infrastructure.observability;

import ch.qos.logback.classic.LoggerContext;
import ch.qos.logback.classic.spi.ILoggingEvent;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import org.springframework.boot.logging.logback.StructuredLogEncoder;
import org.springframework.core.env.Environment;
import org.springframework.core.env.PropertiesPropertySource;
import org.springframework.core.env.StandardEnvironment;
import org.springframework.core.io.ClassPathResource;
import org.springframework.core.io.support.PropertiesLoaderUtils;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.json.JsonMapper;

/**
 * Renders a captured log event exactly as the base configuration writes it to the console (ECS JSON, with the
 * application.properties renames), without re-initializing the JVM-wide logging system.
 */
final class EcsLogRenderer {

    private EcsLogRenderer() {}

    static JsonNode render(ILoggingEvent event) {
        try {
            var properties = PropertiesLoaderUtils.loadProperties(new ClassPathResource("application.properties"));
            var environment = new StandardEnvironment();
            environment.getPropertySources().addFirst(new PropertiesPropertySource("application", properties));
            var context = new LoggerContext();
            context.putObject(Environment.class.getName(), environment);
            var encoder = new StructuredLogEncoder();
            encoder.setContext(context);
            encoder.setFormat(properties.getProperty("logging.structured.format.console"));
            encoder.start();
            var line = new String(encoder.encode(event), StandardCharsets.UTF_8);
            return JsonMapper.builder().build().readTree(line);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }
}
