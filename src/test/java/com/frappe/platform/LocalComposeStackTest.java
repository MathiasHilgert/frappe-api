package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.regex.Pattern;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.yaml.snakeyaml.Yaml;

/**
 * The local stack contract of {@code compose.yaml}: one {@code docker compose up -d --wait} starts everything, and
 * every published host port can be moved by an environment variable when another project already uses it.
 */
class LocalComposeStackTest {

    private static final Pattern OVERRIDABLE_PORT = Pattern.compile("\\$\\{FRAPPE_[A-Z0-9_]+_PORT:-\\d+}:\\d+");

    private static Map<String, Map<String, Object>> services;

    @SuppressWarnings("unchecked")
    @BeforeAll
    static void readCompose() throws IOException {
        try (var reader = Files.newBufferedReader(Path.of("compose.yaml"))) {
            Map<String, Object> compose = new Yaml().load(reader);
            services = (Map<String, Map<String, Object>>) compose.get("services");
        }
    }

    @SuppressWarnings("unchecked")
    @Test
    void everyPublishedHostPortIsOverridableByAnEnvironmentVariable() {
        // Given
        var ports = services.values().stream()
                .flatMap(service -> ((List<String>) service.getOrDefault("ports", List.of())).stream())
                .toList();

        // Then
        assertThat(ports)
                .isNotEmpty()
                .allMatch(port -> OVERRIDABLE_PORT.matcher(port).matches());
    }

    @SuppressWarnings("unchecked")
    @Test
    void runsValkeyAsTheRedisServiceConnection() {
        // When
        var valkey = services.get("valkey");

        // Then Boot does not recognise the valkey image by name, so the label names the connection type
        assertThat(valkey).isNotNull();
        assertThat(valkey.get("image")).isEqualTo("valkey/valkey:9-alpine");
        assertThat((Map<String, String>) valkey.get("labels"))
                .containsEntry("org.springframework.boot.service-connection", "redis");
        assertThat((List<String>) valkey.get("ports")).containsExactly("${FRAPPE_VALKEY_PORT:-6379}:6379");
        assertThat(valkey).containsKey("healthcheck");
    }

    @SuppressWarnings("unchecked")
    @Test
    void runsMailpitForLocalMail() {
        // When
        var mailpit = services.get("mailpit");

        // Then SMTP on 1025 and the web UI on 8025, both movable
        assertThat(mailpit).isNotNull();
        assertThat(mailpit.get("image")).isEqualTo("axllent/mailpit:v1.31");
        assertThat((List<String>) mailpit.get("ports"))
                .containsExactly("${FRAPPE_MAILPIT_SMTP_PORT:-1025}:1025", "${FRAPPE_MAILPIT_UI_PORT:-8025}:8025");
    }
}
