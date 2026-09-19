package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;

/**
 * Requests the servlet container refuses before Spring sees them (a garbled request line, an invalid path, TRACE) get
 * the same problem shape as everything else, never Tomcat's HTML error page.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@AutoConfigureRestTestClient
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, ProblemRoutes.class})
@ActiveProfiles("local")
class ContainerProblemsTests {

    @LocalServerPort
    int port;

    @ParameterizedTest
    @ValueSource(
            strings = {
                "GARBAGE\r\n\r\n",
                "GET /v1/% HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n",
                "GET /v1/a|b HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n",
                "GET /nope HTTP/1.1\r\nHost: [[::bad\r\nConnection: close\r\n\r\n"
            })
    void aRequestTheContainerRefusesIsTheInvalidRequestProblem(String request) throws Exception {
        // When
        var answer = RawHttp.exchange(port, request);

        // Then
        assertThat(answer)
                .startsWith("HTTP/1.1 400")
                .contains("Content-Type: application/problem+json")
                .contains("\"type\":\"https://frappe.app/problems/invalid-request\"")
                .contains("\"code\":\"invalid-request\"")
                .contains("\"title\":\"Invalid request\"")
                .doesNotContainIgnoringCase("html")
                .doesNotContainIgnoringCase("tomcat");
    }

    @Test
    void traceIsAMethodNotAllowedProblemWithTheUsualHeaders() throws Exception {
        // When
        var answer = RawHttp.exchange(
                port,
                "TRACE /v1/test/problems/read-only HTTP/1.1\r\nHost: localhost\r\nAccept-Language: es\r\n"
                        + "X-Secret: abc\r\nConnection: close\r\n\r\n");

        // Then
        assertThat(answer)
                .startsWith("HTTP/1.1 405")
                .contains("Content-Type: application/problem+json")
                .contains("Content-Language: es")
                .contains("Vary: Accept-Language")
                .contains("X-Content-Type-Options: nosniff")
                .contains("X-Frame-Options: DENY")
                .contains("\"code\":\"method-not-allowed\"")
                .contains("\"instance\":\"/v1/test/problems/read-only\"")
                .doesNotContain("X-Secret");
    }
}
