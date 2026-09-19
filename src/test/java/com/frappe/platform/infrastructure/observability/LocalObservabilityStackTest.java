package com.frappe.platform.infrastructure.observability;

import static org.assertj.core.api.Assertions.assertThat;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.yaml.snakeyaml.Yaml;

/**
 * The zero-config contract of the local stack: Spring Boot's Docker Compose support wires OTLP traces, metrics and logs
 * to a service only when its image is {@code grafana/otel-lgtm}, using the standard OTLP ports inside the container (the
 * host ports default to the same numbers and can be moved).
 */
class LocalObservabilityStackTest {

    @SuppressWarnings("unchecked")
    @Test
    void composeRunsGrafanaOtelLgtmOnTheStandardPorts() throws IOException {
        // Given
        Map<String, Map<String, Map<String, Object>>> compose;
        try (var reader = Files.newBufferedReader(Path.of("compose.yaml"))) {
            compose = new Yaml().load(reader);
        }

        // When
        var lgtm = compose.get("services").get("otel-lgtm");

        // Then
        assertThat(lgtm).isNotNull();
        assertThat((String) lgtm.get("image")).startsWith("grafana/otel-lgtm:");
        assertThat((List<Object>) lgtm.get("ports"))
                .containsExactlyInAnyOrder(
                        "${FRAPPE_GRAFANA_PORT:-3000}:3000",
                        "${FRAPPE_OTLP_GRPC_PORT:-4317}:4317",
                        "${FRAPPE_OTLP_HTTP_PORT:-4318}:4318");
    }
}
