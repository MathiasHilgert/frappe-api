package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.OneTimeTask;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Duration;
import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;

/** A failing task run by the application's own scheduler is logged exactly once per failure. */
@SpringBootTest(
        properties = {
            "db-scheduler.polling-interval=100ms",
            "frappe.scheduling.initial-backoff=200ms",
            "frappe.scheduling.max-retries=1"
        })
@Import({TestcontainersConfiguration.class, SchedulingProbes.class})
@ActiveProfiles("local")
class SchedulingLogIntegrationTests {

    final String key = "probe-" + UUID.randomUUID();

    final Logger root = (Logger) org.slf4j.LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);

    final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    @Autowired
    OneTimeTask<String> failingProbe;

    @Autowired
    MeterRegistry meters;

    @BeforeEach
    void captureLogs() {
        logs.start();
        root.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        root.detachAppender(logs);
    }

    @Test
    void everyFailureAndTheGiveUpAreLoggedExactlyOnce() {
        // Given
        var givenUpBefore = givenUp();

        // When one retry is allowed and both runs fail
        failingProbe.schedule(key, key, Instant.now());

        // Then
        await().atMost(Duration.ofSeconds(20)).until(() -> givenUp() == givenUpBefore + 1);
        assertThat(logs.list)
                .filteredOn(event -> event.getLevel().isGreaterOrEqual(Level.WARN) && mentionsTheRun(event))
                .extracting(ILoggingEvent::getLevel)
                .containsExactly(Level.WARN, Level.ERROR);
    }

    private double givenUp() {
        return meters.counter(
                        RetryingFailureHandler.EXHAUSTED, ObservedTaskExecution.TASK_NAME, "platform.failing-probe")
                .count();
    }

    private boolean mentionsTheRun(ILoggingEvent event) {
        var thrown = event.getThrowableProxy();
        return event.getFormattedMessage().contains(key)
                || (thrown != null
                        && thrown.getMessage() != null
                        && thrown.getMessage().contains(key));
    }
}
