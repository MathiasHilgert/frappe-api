package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import org.springframework.test.web.servlet.assertj.MvcTestResult;
import tools.jackson.databind.json.JsonMapper;

/** Refusals of the security chain, decided before any controller runs, are localized problems too. */
@SpringBootTest
@AutoConfigureMockMvc
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, ProblemRoutes.class})
@ActiveProfiles("local")
class SecurityProblemsTests {

    static final String PROBLEMS = "https://frappe.app/problems/";

    @Autowired
    MockMvcTester http;

    @Autowired
    JsonMapper json;

    @Test
    void noSessionOnAnAuthenticatedRouteIsAnUnauthenticatedProblem() {
        // When
        var result = http.get().uri(ProblemRoutes.SELF_PATH).exchange();

        // Then
        assertThat(result)
                .hasStatus(HttpStatus.UNAUTHORIZED)
                .hasContentType(MediaType.APPLICATION_PROBLEM_JSON)
                .hasHeader(HttpHeaders.WWW_AUTHENTICATE, "Bearer")
                .hasHeader(HttpHeaders.CONTENT_LANGUAGE, "en");
        var problem = problemOf(result);
        assertThat(problem)
                .containsEntry("type", PROBLEMS + "unauthenticated")
                .containsEntry("code", "unauthenticated")
                .containsEntry("status", 401)
                .containsEntry("title", "Authentication required")
                .containsEntry("params", Map.of());
        assertThat((String) problem.get("traceId")).matches("[0-9a-f]{32}");
    }

    @ParameterizedTest(name = "{0} -> {1}")
    @CsvSource({"es, Autenticación requerida", "pt, Autenticação necessária", "xx, Authentication required"})
    void anUnresolvableTokenIsAnUnauthenticatedProblemInTheRequestsLanguage(String language, String title) {
        // When
        var result = http.get()
                .uri(ProblemRoutes.SELF_PATH)
                .header(HttpHeaders.AUTHORIZATION, "Bearer unknown-token")
                .header(HttpHeaders.ACCEPT_LANGUAGE, language)
                .exchange();

        // Then
        assertThat(result).hasStatus(HttpStatus.UNAUTHORIZED).hasContentType(MediaType.APPLICATION_PROBLEM_JSON);
        assertThat(problemOf(result))
                .containsEntry("title", title)
                .containsEntry("code", "unauthenticated")
                .containsEntry("type", PROBLEMS + "unauthenticated");
    }

    @Test
    void aSessionOnAPathTheChainRefusesIsAForbiddenProblem() {
        // Given actuator endpoints other than health are closed for everyone

        // When
        var result = http.get()
                .uri("/actuator")
                .header(HttpHeaders.AUTHORIZATION, "Bearer " + ProblemRoutes.TOKEN)
                .exchange();

        // Then
        assertThat(result).hasStatus(HttpStatus.FORBIDDEN).hasContentType(MediaType.APPLICATION_PROBLEM_JSON);
        assertThat(problemOf(result))
                .containsEntry("type", PROBLEMS + "forbidden")
                .containsEntry("code", "forbidden")
                .containsEntry("title", "Access denied");
    }

    @Test
    void aResolvedSessionStillReachesTheRoute() {
        assertThat(http.get()
                        .uri(ProblemRoutes.SELF_PATH)
                        .header(HttpHeaders.AUTHORIZATION, "Bearer " + ProblemRoutes.TOKEN))
                .hasStatusOk();
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> problemOf(MvcTestResult result) {
        return json.readValue(
                new String(result.getResponse().getContentAsByteArray(), StandardCharsets.UTF_8), Map.class);
    }
}
