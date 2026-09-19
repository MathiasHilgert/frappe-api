package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import java.nio.charset.StandardCharsets;
import java.util.List;
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

/**
 * Framework failures (unknown path, unsupported method, unreadable or invalid body) answer RFC 9457 problems with a
 * stable type and code, a trace id and text in the request's language, never Spring Boot's default error JSON.
 */
@SpringBootTest
@AutoConfigureMockMvc
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, ProblemRoutes.class})
@ActiveProfiles("local")
class FrameworkProblemsTests {

    static final String PROBLEMS = "https://frappe.app/problems/";

    @Autowired
    MockMvcTester http;

    @Autowired
    JsonMapper json;

    @Test
    void anUnknownPathIsANotFoundProblem() {
        // When
        var result = http.get().uri("/v1/test/problems/missing").exchange();

        // Then
        assertThat(result)
                .hasStatus(HttpStatus.NOT_FOUND)
                .hasContentType(MediaType.APPLICATION_PROBLEM_JSON)
                .hasHeader(HttpHeaders.CONTENT_LANGUAGE, "en");
        var problem = problemOf(result);
        assertThat(problem)
                .containsOnlyKeys("type", "title", "status", "detail", "instance", "code", "params", "traceId")
                .containsEntry("type", PROBLEMS + "not-found")
                .containsEntry("code", "not-found")
                .containsEntry("status", 404)
                .containsEntry("title", "Not found")
                .containsEntry("detail", "There is nothing at this address.")
                .containsEntry("params", Map.of());
        assertThat((String) problem.get("traceId")).matches("[0-9a-f]{32}");
    }

    @ParameterizedTest(name = "{0} -> {1}")
    @CsvSource({"es, es, No encontrado", "pt-BR, pt, Não encontrado", "xx, en, Not found", "'', en, Not found"})
    void theTextIsInTheRequestsLanguageWhileTypeCodeAndParamsStay(String asked, String answered, String title) {
        // When
        var result = http.get()
                .uri("/v1/test/problems/missing")
                .header(HttpHeaders.ACCEPT_LANGUAGE, asked)
                .exchange();

        // Then
        assertThat(result).hasHeader(HttpHeaders.CONTENT_LANGUAGE, answered);
        assertThat(problemOf(result))
                .containsEntry("title", title)
                .containsEntry("type", PROBLEMS + "not-found")
                .containsEntry("code", "not-found")
                .containsEntry("params", Map.of());
    }

    @Test
    void anUnsupportedMethodIsAMethodNotAllowedProblemNamingTheAllowedMethods() {
        // When
        var result = http.post().uri("/v1/test/problems/read-only").exchange();

        // Then
        assertThat(result).hasStatus(HttpStatus.METHOD_NOT_ALLOWED).hasContentType(MediaType.APPLICATION_PROBLEM_JSON);
        assertThat(result.getResponse().getHeader(HttpHeaders.ALLOW)).contains("GET");
        assertThat(problemOf(result))
                .containsEntry("type", PROBLEMS + "method-not-allowed")
                .containsEntry("code", "method-not-allowed")
                .containsEntry("title", "Method not allowed");
    }

    @Test
    void anUnreadableBodyIsAnInvalidRequestProblemWithoutParserDetails() {
        // When
        var result = http.post()
                .uri(ProblemRoutes.ORDERS_PATH)
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"name\": \"Ana\", \"lines\": [")
                .exchange();

        // Then
        assertThat(result).hasStatus(HttpStatus.BAD_REQUEST).hasContentType(MediaType.APPLICATION_PROBLEM_JSON);
        var problem = problemOf(result);
        assertThat(problem)
                .containsEntry("type", PROBLEMS + "invalid-request")
                .containsEntry("code", "invalid-request")
                .containsEntry("title", "Invalid request")
                .doesNotContainKey("errors");
        assertThat(bodyOf(result))
                .doesNotContainIgnoringCase("jackson")
                .doesNotContainIgnoringCase("exception")
                .doesNotContain("OrderRequest");
    }

    @Test
    void anInvalidBodyListsOneErrorPerViolatedFieldSortedByItsJsonPointer() {
        // When
        var result = postInvalidOrder("en");

        // Then
        assertThat(result).hasStatus(HttpStatus.BAD_REQUEST).hasContentType(MediaType.APPLICATION_PROBLEM_JSON);
        var problem = problemOf(result);
        assertThat(problem)
                .containsEntry("type", PROBLEMS + "invalid-request")
                .containsEntry("code", "invalid-request")
                .containsEntry("detail", "4 fields are invalid.");
        // Sorted by pointer; params carry only client-meaningful scalars (never a pattern, flags or classes)
        assertThat(errorsOf(problem))
                .containsExactly(
                        Map.of(
                                "pointer",
                                "/code",
                                "code",
                                "size",
                                "params",
                                Map.of("max", 5, "min", 2),
                                "detail",
                                "must have between 2 and 5 characters or items"),
                        Map.of(
                                "pointer",
                                "/lines/0/quantity",
                                "code",
                                "min",
                                "params",
                                Map.of("value", 1),
                                "detail",
                                "must be at least 1"),
                        Map.of(
                                "pointer",
                                "/name",
                                "code",
                                "not-blank",
                                "params",
                                Map.of(),
                                "detail",
                                "must not be blank"),
                        Map.of(
                                "pointer",
                                "/tag",
                                "code",
                                "pattern",
                                "params",
                                Map.of(),
                                "detail",
                                "has an invalid format"));
    }

    @Test
    void validationErrorsKeepTheirCodesAndParamsInEveryLanguage() {
        // When
        var spanish = problemOf(postInvalidOrder("es"));
        var portuguese = problemOf(postInvalidOrder("pt"));

        // Then
        assertThat(spanish).containsEntry("detail", "4 campos no son válidos.");
        assertThat(portuguese).containsEntry("detail", "4 campos são inválidos.");
        assertThat(errorsOf(spanish))
                .extracting(error -> error.get("pointer") + " " + error.get("code") + " " + error.get("detail"))
                .contains("/lines/0/quantity min debe ser como mínimo 1");
        assertThat(errorsOf(portuguese))
                .extracting(error -> error.get("pointer") + " " + error.get("code") + " " + error.get("detail"))
                .contains("/lines/0/quantity min deve ser no mínimo 1");
        assertThat(errorsOf(spanish))
                .extracting(error -> error.get("pointer") + " " + error.get("code") + " " + error.get("params"))
                .containsExactlyInAnyOrderElementsOf(errorsOf(portuguese).stream()
                        .map(error -> error.get("pointer") + " " + error.get("code") + " " + error.get("params"))
                        .toList());
    }

    private MvcTestResult postInvalidOrder(String language) {
        return http.post()
                .uri(ProblemRoutes.ORDERS_PATH)
                .header(HttpHeaders.ACCEPT_LANGUAGE, language)
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"name\": \" \", \"code\": \"x\", \"lines\": [{\"quantity\": 0}], \"tag\": \"a1\"}")
                .exchange();
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> problemOf(MvcTestResult result) {
        return json.readValue(bodyOf(result), Map.class);
    }

    private static String bodyOf(MvcTestResult result) {
        return new String(result.getResponse().getContentAsByteArray(), StandardCharsets.UTF_8);
    }

    @SuppressWarnings("unchecked")
    private static List<Map<String, Object>> errorsOf(Map<String, Object> problem) {
        return (List<Map<String, Object>>) problem.get("errors");
    }
}
