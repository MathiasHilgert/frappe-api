package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.probe.infrastructure.web.ProbeWebRoutes;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.slf4j.LoggerFactory;
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

/** A use case's business failure reaches the client as the problem its module mapped it to, localized. */
@SpringBootTest
@AutoConfigureMockMvc
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, ProblemRoutes.class, ProbeWebRoutes.class})
@ActiveProfiles("local")
class RefusedRequestProblemsTests {

    @Autowired
    MockMvcTester http;

    @Autowired
    JsonMapper json;

    ListAppender<ILoggingEvent> logs = new ListAppender<>();

    @BeforeEach
    void captureLogs() {
        logs.start();
        logger().addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        logger().detachAppender(logs);
    }

    @Test
    void aUseCaseFailureIsAnsweredWithTheProblemItsModuleMappedItTo() {
        // When
        var result =
                http.post().uri(ProbeWebRoutes.PROBES_PATH + "?ending=FAILURE").exchange();

        // Then
        assertThat(result)
                .hasStatus(HttpStatus.CONFLICT)
                .hasContentType(MediaType.APPLICATION_PROBLEM_JSON)
                .hasHeader(HttpHeaders.CONTENT_LANGUAGE, "en");
        var problem = problemOf(result);
        assertThat(problem)
                .containsOnlyKeys("type", "title", "status", "detail", "instance", "code", "params", "traceId")
                .containsEntry("type", "https://frappe.app/problems/probe-rejected")
                .containsEntry("code", "probe-rejected")
                .containsEntry("status", 409)
                .containsEntry("title", "Probe rejected")
                .containsEntry("detail", "At most 3 probes are allowed.")
                .containsEntry("params", Map.of("limit", ProbeWebRoutes.PROBE_LIMIT));
        assertThat((String) problem.get("traceId")).matches("[0-9a-f]{32}");
        assertThat(logs.list).noneMatch(event -> event.getLevel() == Level.ERROR);
    }

    @ParameterizedTest(name = "{0}")
    @CsvSource({
        "es, Sonda rechazada, Se permiten como máximo 3 sondas.",
        "pt-BR, Sonda recusada, São permitidas no máximo 3 sondas.",
        "xx, Probe rejected, At most 3 probes are allowed."
    })
    void theMappedProblemIsInTheRequestsLanguageWithTheSameCodeAndParams(String language, String title, String detail) {
        // When
        var result = http.post()
                .uri(ProbeWebRoutes.PROBES_PATH + "?ending=FAILURE")
                .header(HttpHeaders.ACCEPT_LANGUAGE, language)
                .exchange();

        // Then
        assertThat(problemOf(result))
                .containsEntry("title", title)
                .containsEntry("detail", detail)
                .containsEntry("code", "probe-rejected")
                .containsEntry("params", Map.of("limit", ProbeWebRoutes.PROBE_LIMIT));
    }

    @Test
    void aSuccessIsAnsweredByTheRoute() {
        assertThat(http.post().uri(ProbeWebRoutes.PROBES_PATH + "?ending=SUCCESS"))
                .hasStatusOk();
    }

    @Test
    void aFailureNoMapperHandlesIsTheGenericInternalErrorAndLogged() {
        // When
        var result = http.post()
                .uri(ProbeWebRoutes.PROBES_PATH + "/misconfigured?mapped=false")
                .exchange();

        // Then
        assertThat(result).hasStatus(HttpStatus.INTERNAL_SERVER_ERROR);
        assertThat(problemOf(result)).containsEntry("code", "internal-error");
        assertThat(bodyOf(result)).doesNotContain("UnmappedError").doesNotContain("SURPRISE");
        assertThat(logs.list)
                .filteredOn(event -> event.getLevel() == Level.ERROR)
                .singleElement()
                .satisfies(event -> assertThat(event.getThrowableProxy().getMessage())
                        .contains(ProbeWebRoutes.UnmappedError.class.getName()));
    }

    @Test
    void aMappedProblemWithoutCatalogTextIsTheGenericInternalErrorAndLogged() {
        // When
        var result = http.post()
                .uri(ProbeWebRoutes.PROBES_PATH + "/misconfigured?mapped=true")
                .exchange();

        // Then
        assertThat(result).hasStatus(HttpStatus.INTERNAL_SERVER_ERROR);
        assertThat(problemOf(result)).containsEntry("code", "internal-error");
        assertThat(logs.list)
                .filteredOn(event -> event.getLevel() == Level.ERROR)
                .singleElement()
                .satisfies(event ->
                        assertThat(event.getThrowableProxy().getMessage()).contains("probe.recording.no-such-key"));
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> problemOf(MvcTestResult result) {
        return json.readValue(bodyOf(result), Map.class);
    }

    private static String bodyOf(MvcTestResult result) {
        return new String(result.getResponse().getContentAsByteArray(), StandardCharsets.UTF_8);
    }

    private static Logger logger() {
        return (Logger) LoggerFactory.getLogger(UnexpectedFailures.class);
    }
}
