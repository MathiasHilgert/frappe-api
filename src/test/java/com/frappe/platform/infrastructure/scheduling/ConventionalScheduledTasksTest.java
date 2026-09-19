package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;

import com.frappe.platform.EntitySchedule;
import com.github.kagkarlsson.scheduler.task.Execution;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.ExecutionOperations;
import com.github.kagkarlsson.scheduler.task.OnStartup;
import com.github.kagkarlsson.scheduler.task.Task;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import com.github.kagkarlsson.scheduler.task.schedule.FixedDelay;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.mockito.ArgumentCaptor;

class ConventionalScheduledTasksTest {

    static final Instant STARTED = Instant.parse("2026-09-18T11:59:00Z");

    static final Instant DONE = Instant.parse("2026-09-18T12:00:00Z");

    static final Duration INITIAL_BACKOFF = Duration.ofSeconds(30);

    static final int MAX_RETRIES = 3;

    final ConventionalScheduledTasks tasks =
            new ConventionalScheduledTasks(new SchedulingProperties(INITIAL_BACKOFF, MAX_RETRIES));

    @ParameterizedTest
    @ValueSource(ints = {0, 1, 2})
    void aFailingRecurringTaskRetriesWithExponentialBackoff(int earlierFailures) {
        // Given
        var task = tasks.recurring("platform.probe", FixedDelay.ofHours(1), (instance, context) -> {});

        // When
        var retryAt = nextExecutionAfterFailure(task, null, earlierFailures);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF.multipliedBy(1L << earlierFailures)));
    }

    @Test
    void aRecurringTaskFallsBackToItsScheduleOnceItsRetriesAreUsedUp() {
        // Given
        var task = tasks.recurring("platform.probe", FixedDelay.ofHours(1), (instance, context) -> {});

        // When
        var retryAt = nextExecutionAfterFailure(task, null, MAX_RETRIES);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(Duration.ofHours(1)));
    }

    @Test
    void aRecurringTaskWithStateStartsWithItsInitialDataAndRetriesWithBackoff() {
        // Given
        var task = tasks.recurring(
                "platform.stateful-probe", FixedDelay.ofHours(1), String.class, "first", (instance, context) -> "next");

        // When
        var retryAt = nextExecutionAfterFailure(task, "first", 1);

        // Then
        assertThat(task).isInstanceOf(OnStartup.class);
        assertThat(task.getDataClass()).isEqualTo(String.class);
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF.multipliedBy(2)));
    }

    @Test
    void aOneTimeTaskKeepsRetryingAtItsLongestBackoffOnceItsRetriesAreUsedUp() {
        // Given
        var task = tasks.oneTime("platform.one-time-probe", String.class, (instance, context) -> {});

        // When
        var retryAt = nextExecutionAfterFailure(task, "payload", MAX_RETRIES + 5);

        // Then
        // No work is lost: after the fast retries it keeps trying at the next backoff step (30s × 2³).
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF.multipliedBy(1L << MAX_RETRIES)));
    }

    @Test
    void aOneTimeTaskRetriesWithExponentialBackoff() {
        // Given
        var task = tasks.oneTime("platform.one-time-probe", String.class, (instance, context) -> {});

        // When
        var retryAt = nextExecutionAfterFailure(task, "payload", 2);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF.multipliedBy(4)));
    }

    @Test
    void aPerEntityTaskRetriesWithBackoffThenFallsBackToTheEntitySchedule() {
        // Given
        var task = tasks.perEntity("platform.entity-probe", (instance, context) -> {});
        var daily = new EntitySchedule("0 0 4 * * *", ZoneId.of("UTC"));

        // When
        var retryAt = nextExecutionAfterFailure(task, daily, 0);
        var fallbackAt = nextExecutionAfterFailure(task, daily, MAX_RETRIES);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF));
        assertThat(fallbackAt).isEqualTo(Instant.parse("2026-09-19T04:00:00Z"));
    }

    @ParameterizedTest
    @ValueSource(
            strings = {"outbox-recovery", "Platform.outbox", "platform.outbox_recovery", "platform.", "platform.a.b"})
    void rejectsATaskNameThatIsNotModuleDotKebabCase(String name) {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> tasks.oneTime(name, String.class, (instance, context) -> {}))
                .withMessageContaining(name)
                .withMessageContaining("<module>.<kebab-case-name>");
    }

    private static <T> Instant nextExecutionAfterFailure(Task<T> task, T data, int earlierFailures) {
        var instance = new TaskInstance<>(task.getName(), "instance", data);
        var execution =
                new Execution(STARTED, instance, true, "instance-a", null, STARTED, earlierFailures, STARTED, 1);
        var failed = ExecutionComplete.failure(execution, STARTED, DONE, new IllegalStateException("boom"));
        @SuppressWarnings("unchecked")
        ExecutionOperations<T> operations = mock(ExecutionOperations.class);
        var next = ArgumentCaptor.forClass(Instant.class);

        task.getFailureHandler().onFailure(failed, operations);

        verify(operations).reschedule(eq(failed), next.capture());
        return next.getValue();
    }
}
