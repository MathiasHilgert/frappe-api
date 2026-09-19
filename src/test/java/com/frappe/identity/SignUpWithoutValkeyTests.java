package com.frappe.identity;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import tools.jackson.databind.json.JsonMapper;

/** Without Valkey the caps cannot be checked, so a sign-up fails closed: the generic 500 and nothing written. */
@SpringBootTest(
        properties = {
            "spring.data.redis.url=redis://localhost:1",
            "spring.data.redis.timeout=500ms",
            "spring.data.redis.connect-timeout=500ms"
        })
@AutoConfigureMockMvc
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class SignUpWithoutValkeyTests {

    @Autowired
    MockMvcTester http;

    @Autowired
    JsonMapper json;

    @Autowired
    JdbcTemplate jdbc;

    @Test
    void aStartIsTheGenericInternalErrorAndWritesNoSignUp() {
        // Given
        var email = "ana-" + UUID.randomUUID() + "@example.com";

        // When
        var result = http.post()
                .uri("/v1/sign-ups")
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"email\": \"%s\"}".formatted(email))
                .exchange();

        // Then
        assertThat(result).hasStatus(HttpStatus.INTERNAL_SERVER_ERROR);
        @SuppressWarnings("unchecked")
        Map<String, Object> problem = json.readValue(result.getResponse().getContentAsByteArray(), Map.class);
        assertThat(problem).containsEntry("code", "internal-error");
        var rows = jdbc.queryForObject(
                "select count(*) from identity.sign_up where lower(email) = ?",
                Integer.class,
                email.toLowerCase(Locale.ROOT));
        assertThat(rows).isZero();
    }
}
