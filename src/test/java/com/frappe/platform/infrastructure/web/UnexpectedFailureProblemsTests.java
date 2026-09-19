package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Duration;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.client.RestTestClient;
import tools.jackson.databind.json.JsonMapper;

/**
 * Infrastructure failures are never visible to clients: whatever a dependency throws (database down, bad SQL, a
 * provider call, NATS or Valkey unreachable, a failing filter before any controller), the client gets the generic,
 * localized internal-error problem, and the details go to one ERROR log line and a metric.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@AutoConfigureRestTestClient
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, ProblemRoutes.class})
@ActiveProfiles("local")
class UnexpectedFailureProblemsTests {

    static final String METRIC = "http.server.unexpected.errors";

    /** Nothing about the failure may reach the client: technology, provider, host, credential, SQL, class or trace. */
    static final List<String> INTERNALS = List.of(
            "exception",
            "error:",
            "caused",
            "java.",
            "org.",
            "com.",
            "io.",
            "at ",
            "postgres",
            "psql",
            "jdbc",
            "sql",
            "select",
            "customer_card",
            "card_number",
            "nats",
            "redis",
            "valkey",
            "lettuce",
            "stripe",
            "charges",
            "sk_live",
            "api_key",
            "127.0.0.1",
            "10.0.0.5",
            "6379",
            "eyj",
            "connection",
            "refused",
            "unreachable");

    @Autowired
    RestTestClient http;

    @Autowired
    JsonMapper json;

    @Autowired
    MeterRegistry meters;

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

    static Stream<Arguments> failures() {
        var kinds = Map.of(
                "database-down", "CannotGetJdbcConnectionException",
                "sql-error", "BadSqlGrammarException",
                "provider", "ResourceAccessException",
                "provider-malformed", "RestClientException",
                "nats", "IOException",
                "valkey", "SecretStoreUnavailableException",
                "filter", "IllegalStateException");
        var titles = Map.of(
                "en", "An unexpected error occurred",
                "es", "Se produjo un error inesperado",
                "pt", "Ocorreu um erro inesperado");
        return kinds.entrySet().stream()
                .flatMap(kind -> titles.entrySet().stream()
                        .map(title -> Arguments.of(kind.getKey(), title.getKey(), title.getValue(), kind.getValue())));
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("failures")
    void anInfrastructureFailureIsTheGenericLocalizedInternalErrorProblem(
            String kind, String language, String title, String exceptionType) {
        // Given
        var before = count(exceptionType);

        // When
        var result = http.get()
                .uri(ProblemRoutes.FAILURES_PATH + kind)
                .header(HttpHeaders.ACCEPT_LANGUAGE, language)
                .exchange()
                .expectStatus()
                .isEqualTo(500)
                .expectHeader()
                .contentType(MediaType.APPLICATION_PROBLEM_JSON)
                .expectHeader()
                .valueEquals(HttpHeaders.CONTENT_LANGUAGE, language)
                .expectBody(String.class)
                .returnResult();

        // Then the client sees the generic problem only
        @SuppressWarnings("unchecked")
        Map<String, Object> problem = json.readValue(result.getResponseBody(), Map.class);
        assertThat(problem)
                .containsOnlyKeys("type", "title", "status", "detail", "instance", "code", "params", "traceId")
                .containsEntry("type", "https://frappe.app/problems/internal-error")
                .containsEntry("code", "internal-error")
                .containsEntry("status", 500)
                .containsEntry("title", title)
                .containsEntry("params", Map.of());
        assertThat((String) problem.get("traceId")).matches("[0-9a-f]{32}");
        // instance is the request's own path, also when a filter failed and the answer came from the error dispatch
        assertThat(problem.remove("instance")).isEqualTo(ProblemRoutes.FAILURES_PATH + kind);
        var visible = json.writeValueAsString(problem).toLowerCase(Locale.ROOT);
        assertThat(INTERNALS).noneMatch(visible::contains);

        // And the details are logged once, at ERROR, with the request and the cause, and counted
        var errors = logs.list.stream()
                .filter(event -> event.getLevel() == Level.ERROR)
                .toList();
        assertThat(errors).hasSize(1);
        var logged = errors.getFirst();
        assertThat(logged.getThrowableProxy().getClassName()).endsWith("." + exceptionType);
        assertThat(logged.getKeyValuePairs())
                .anySatisfy(pair -> assertThat(pair.key + "=" + pair.value).isEqualTo("http.request.method=GET"))
                .anySatisfy(pair -> assertThat(pair.key + "=" + pair.value)
                        .isEqualTo("url.path=" + ProblemRoutes.FAILURES_PATH + kind));
        assertThat(count(exceptionType)).isEqualTo(before + 1);
        // and the request's own observation carries the failure (http.server.requests, exception tag), recorded once
        // the server finished the exchange
        await().atMost(Duration.ofSeconds(5))
                .untilAsserted(() -> assertThat(
                                meters.find("http.server.requests").timers())
                        .as("http.server.requests with exception=%s", exceptionType)
                        .anySatisfy(timer ->
                                assertThat(timer.getId().getTag("exception")).isEqualTo(exceptionType)));
    }

    private double count(String exceptionType) {
        var counter = meters.find(METRIC).tag("error", exceptionType).counter();
        return counter == null ? 0 : counter.count();
    }

    private static Logger logger() {
        return (Logger) LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);
    }
}
