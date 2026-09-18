package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Duration;
import java.util.Arrays;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.builder.SpringApplicationBuilder;
import org.springframework.boot.test.system.CapturedOutput;
import org.springframework.boot.test.system.OutputCaptureExtension;
import org.springframework.context.annotation.Configuration;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.json.JsonMapper;

@ExtendWith(OutputCaptureExtension.class)
class StructuredLoggingTests {

    private static final String UNREACHABLE_URL = "nats://localhost:1";

    /** Loads only the base configuration: no beans, no profile. */
    @Configuration(proxyBeanMethods = false)
    static class BaseConfigurationOnly {}

    @Test
    void logsNatsUnavailableAsEcsJsonWithTheUrlAsAField(CapturedOutput output) {
        // Given the base configuration (no 'local' profile) has initialized logging
        try (var context = new SpringApplicationBuilder(BaseConfigurationOnly.class)
                .web(WebApplicationType.NONE)
                .properties(
                        "FRAPPE_DB_URL=jdbc:postgresql://unused/db",
                        "FRAPPE_APP_PASSWORD=unused",
                        "FRAPPE_OWNER_PASSWORD=unused")
                .run()) {
            var client = new NatsClient(
                    new NatsProperties(
                            UNREACHABLE_URL,
                            "test",
                            Duration.ofMillis(100),
                            Duration.ofSeconds(1),
                            Duration.ofSeconds(1)),
                    connection -> {});

            // When the client starts without a reachable NATS
            client.start();
            client.close();
        }

        // Then the warning is one ECS JSON line with the URL as a structured field
        var line = warningLine(output);
        assertThat(line.path("log").path("level").asString()).isEqualTo("WARN");
        assertThat(line.path("natsUrl").asString()).isEqualTo(UNREACHABLE_URL);
        assertThat(line.path("ecs").path("version").isMissingNode()).isFalse();
    }

    private static JsonNode warningLine(CapturedOutput output) {
        var json = Arrays.stream(output.getOut().split("\\R"))
                .filter(line -> line.startsWith("{") && line.contains("NATS is unavailable"))
                .findFirst()
                .orElseThrow(() -> new AssertionError("No ECS JSON line for the NATS warning in:\n" + output));
        return JsonMapper.builder().build().readTree(json);
    }
}
