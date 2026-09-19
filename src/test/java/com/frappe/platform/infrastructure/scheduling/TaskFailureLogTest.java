package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.tuple;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.github.kagkarlsson.scheduler.task.Execution;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import java.time.Duration;
import java.time.Instant;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;

class TaskFailureLogTest {

    static final Instant STARTED = Instant.parse("2026-09-18T11:59:00Z");

    static final Instant DONE = Instant.parse("2026-09-18T12:00:00Z");

    static final int MAX_RETRIES = 3;

    final TaskFailureLog failureLog = new TaskFailureLog(new SchedulingProperties(Duration.ofSeconds(30), MAX_RETRIES));

    final Logger logger = (Logger) LoggerFactory.getLogger(TaskFailureLog.class);

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
    void aFailureWithRetriesLeftIsLoggedOnceAtWarnWithItsContext() {
        // Given
        var cause = new IllegalStateException("database unavailable");

        // When
        failureLog.onExecutionComplete(failed(1, cause));

        // Then
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getFormattedMessage()).contains("platform.probe");
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("database unavailable");
            assertThat(event.getKeyValuePairs())
                    .extracting(pair -> pair.key, pair -> String.valueOf(pair.value))
                    .contains(
                            tuple("frappe.scheduling.task_name", "platform.probe"),
                            tuple("frappe.scheduling.task_instance", "branch-42"),
                            tuple("frappe.scheduling.consecutive_failures", "2"));
        });
    }

    @Test
    void aFailureAfterTheRetriesAreUsedUpIsLoggedAtError() {
        // When
        failureLog.onExecutionComplete(failed(MAX_RETRIES, new IllegalStateException("still failing")));

        // Then
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.ERROR);
            assertThat(event.getKeyValuePairs())
                    .extracting(pair -> pair.key, pair -> String.valueOf(pair.value))
                    .contains(tuple("frappe.scheduling.consecutive_failures", "4"));
        });
    }

    @Test
    void aSuccessfulExecutionIsNotLogged() {
        // When
        failureLog.onExecutionComplete(ExecutionComplete.success(execution(0), STARTED, DONE));

        // Then
        assertThat(logs.list).isEmpty();
    }

    private static ExecutionComplete failed(int earlierFailures, Throwable cause) {
        return ExecutionComplete.failure(execution(earlierFailures), STARTED, DONE, cause);
    }

    private static Execution execution(int earlierFailures) {
        var instance = new TaskInstance<>("platform.probe", "branch-42");
        return new Execution(STARTED, instance, true, "instance-a", null, null, earlierFailures, STARTED, 1);
    }
}
