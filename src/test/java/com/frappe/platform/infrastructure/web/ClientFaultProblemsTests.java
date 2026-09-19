package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Duration;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;

/**
 * A request the client botched (a body cut short, broken chunking) is the client's fault: a 4xx problem, or no answer
 * when the connection is gone, and never an ERROR log line or a count of unexpected failures.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@AutoConfigureRestTestClient
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, ProblemRoutes.class})
@ActiveProfiles("local")
class ClientFaultProblemsTests {

    @LocalServerPort
    int port;

    @Autowired
    MeterRegistry meters;

    ListAppender<ILoggingEvent> logs = new ListAppender<>();

    @BeforeEach
    void captureLogs() {
        logs.start();
        root().addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        root().detachAppender(logs);
    }

    @Test
    void aTruncatedBodyIsTheClientsFault() throws Exception {
        // Given
        var before = unexpectedFailures();

        // When the body is shorter than announced and the client stops sending
        var answer = RawHttp.exchange(port, request("Content-Length: 60\r\n", "{\"name\": \"Ana\""));

        // Then an answer, if the connection still carries one, is the invalid-request problem
        if (!answer.isEmpty()) {
            assertThat(answer).startsWith("HTTP/1.1 400").contains("\"code\":\"invalid-request\"");
        }
        assertNothingRecorded(before);
    }

    @Test
    void aBrokenChunkedBodyIsTheInvalidRequestProblem() throws Exception {
        // Given
        var before = unexpectedFailures();

        // When a chunk size is not hexadecimal
        var answer = RawHttp.exchange(port, request("Transfer-Encoding: chunked\r\n", "ZZ\r\nabc\r\n0\r\n\r\n"));

        // Then
        assertThat(answer)
                .startsWith("HTTP/1.1 400")
                .contains("application/problem+json")
                .contains("\"code\":\"invalid-request\"");
        assertNothingRecorded(before);
    }

    private void assertNothingRecorded(double before) throws InterruptedException {
        Thread.sleep(Duration.ofMillis(300)); // the server may log after the answer went out
        assertThat(logs.list).noneMatch(event -> event.getLevel() == Level.ERROR);
        assertThat(unexpectedFailures()).isEqualTo(before);
    }

    private static String request(String framing, String body) {
        return "POST " + ProblemRoutes.ORDERS_PATH + " HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n"
                + "Content-Type: application/json\r\n" + framing + "\r\n" + body;
    }

    private double unexpectedFailures() {
        return meters.find(UnexpectedFailures.METRIC).counters().stream()
                .mapToDouble(Counter::count)
                .sum();
    }

    private static Logger root() {
        return (Logger) LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);
    }
}
