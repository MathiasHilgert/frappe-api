package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.tuple;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.github.kagkarlsson.scheduler.task.Execution;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.ExecutionOperations;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import java.time.Duration;
import java.time.Instant;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;

class RetryingFailureHandlerTest {

    static final Instant STARTED = Instant.parse("2026-09-18T11:59:00Z");

    static final Instant DONE = Instant.parse("2026-09-18T12:00:00Z");

    static final int MAX_RETRIES = 3;

    final SchedulingProperties properties = new SchedulingProperties(Duration.ofSeconds(30), MAX_RETRIES);

    final SimpleMeterRegistry meters = new SimpleMeterRegistry();

    @SuppressWarnings("unchecked")
    final ExecutionOperations<String> operations = mock(ExecutionOperations.class);

    final Logger logger = (Logger) LoggerFactory.getLogger(RetryingFailureHandler.class);

    final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    @BeforeEach
    void captureLogs() {
        logs.start();
        logger.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        logger.detachAppender(logs);
    }

    @Test
    void aRecurringRetryWaitsNoLongerThanTheNextRegularRun() {
        // Given a task that runs every minute anyway
        var handler = RetryingFailureHandler.<String>recurring(properties, complete -> DONE.plusSeconds(60));
        var failed = failed(2);

        // When the third failure would back off 2 minutes
        handler.onFailure(failed, operations);

        // Then
        verify(operations).reschedule(failed, DONE.plusSeconds(60));
    }

    @Test
    void aFailureWithRetriesLeftIsLoggedOnceAtWarnWithItsContext() {
        // Given
        var handler = RetryingFailureHandler.<String>recurring(properties, complete -> DONE.plusSeconds(3600));

        // When
        handler.onFailure(failed(1), operations);

        // Then
        verify(operations).reschedule(any(), any());
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getFormattedMessage()).contains("platform.probe");
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("database unavailable");
            assertThat(event.getKeyValuePairs())
                    .extracting(pair -> pair.key, pair -> String.valueOf(pair.value))
                    .contains(
                            tuple("frappe.scheduling.task_name", "platform.probe"),
                            tuple("frappe.scheduling.task_instance", "report-42"),
                            tuple("frappe.scheduling.consecutive_failures", "2"));
        });
    }

    @Test
    void aRecurringTaskOutOfRetriesContinuesOnItsScheduleAndLogsAtError() {
        // Given
        var handler = RetryingFailureHandler.<String>recurring(properties, complete -> DONE.plusSeconds(3600));
        var failed = failed(MAX_RETRIES);

        // When
        handler.onFailure(failed, operations);

        // Then
        verify(operations).reschedule(failed, DONE.plusSeconds(3600));
        assertThat(logs.list)
                .singleElement()
                .extracting(ILoggingEvent::getLevel)
                .isEqualTo(Level.ERROR);
    }

    @Test
    void aOneTimeTaskOutOfRetriesIsRemovedLoggedOnceAtErrorAndCounted() {
        // Given
        var handler = RetryingFailureHandler.<String>oneTime(properties, meters, "platform.probe");
        var failed = failed(MAX_RETRIES);

        // When
        handler.onFailure(failed, operations);

        // Then
        verify(operations).remove();
        verify(operations, never()).reschedule(any(), any());
        assertThat(meters.get("scheduled.task.exhausted")
                        .tag("scheduled.task.name", "platform.probe")
                        .counter()
                        .count())
                .isEqualTo(1.0);
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.ERROR);
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("database unavailable");
            assertThat(event.getKeyValuePairs())
                    .extracting(pair -> pair.key, pair -> String.valueOf(pair.value))
                    .contains(
                            tuple("frappe.scheduling.task_name", "platform.probe"),
                            tuple("frappe.scheduling.task_instance", "report-42"),
                            tuple("frappe.scheduling.consecutive_failures", "4"));
        });
    }

    @Test
    void theExhaustedCounterOfAOneTimeTaskExistsBeforeItsFirstFailure() {
        // When
        RetryingFailureHandler.<String>oneTime(properties, meters, "platform.probe");

        // Then
        assertThat(meters.get("scheduled.task.exhausted")
                        .tag("scheduled.task.name", "platform.probe")
                        .counter()
                        .count())
                .isZero();
    }

    @Test
    void aOneTimeTaskWithRetriesLeftBacksOffExponentially() {
        // Given
        var handler = RetryingFailureHandler.<String>oneTime(properties, meters, "platform.probe");
        var failed = failed(2);

        // When
        handler.onFailure(failed, operations);

        // Then
        verify(operations).reschedule(failed, DONE.plusSeconds(120));
        verify(operations, never()).remove();
    }

    private static ExecutionComplete failed(int earlierFailures) {
        var instance = new TaskInstance<>("platform.probe", "report-42", "payload");
        var execution =
                new Execution(STARTED, instance, true, "instance-a", null, STARTED, earlierFailures, STARTED, 1);
        return ExecutionComplete.failure(execution, STARTED, DONE, new IllegalStateException("database unavailable"));
    }
}
