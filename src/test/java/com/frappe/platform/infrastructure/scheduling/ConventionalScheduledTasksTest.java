package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;

import com.frappe.platform.ScheduledTask;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.task.Execution;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.ExecutionOperations;
import com.github.kagkarlsson.scheduler.task.OnStartup;
import com.github.kagkarlsson.scheduler.task.Task;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.util.ArrayList;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.mockito.ArgumentCaptor;

class ConventionalScheduledTasksTest {

    static final Instant STARTED = Instant.parse("2026-09-18T11:59:00Z");

    static final Instant DONE = Instant.parse("2026-09-18T12:00:00Z");

    static final Duration INITIAL_BACKOFF = Duration.ofSeconds(30);

    static final int MAX_RETRIES = 3;

    static final TaskSchedule HOURLY = TaskSchedule.fixedDelay(Duration.ofHours(1));

    final ConventionalScheduledTasks tasks = new ConventionalScheduledTasks(
            new SchedulingProperties(INITIAL_BACKOFF, MAX_RETRIES),
            () -> mock(Scheduler.class),
            Clock.fixed(DONE, ZoneOffset.UTC));

    @Test
    void aRecurringTaskRunsItsActionAndIsScheduledAtStartup() {
        // Given
        var runs = new ArrayList<String>();
        var task = tasks.recurring(TaskName.of("platform.probe"), HOURLY, () -> runs.add("ran"));

        // When
        library(task).execute(new TaskInstance<>("platform.probe", "recurring"), null);

        // Then
        assertThat(task.name()).isEqualTo(TaskName.of("platform.probe"));
        assertThat(library(task)).isInstanceOf(OnStartup.class);
        assertThat(runs).containsExactly("ran");
    }

    @Test
    void aRecurringTaskWithStateHandsTheReturnedDataToItsNextRun() {
        // Given
        var task = tasks.recurring(
                TaskName.of("platform.stateful-probe"), HOURLY, String.class, "first", data -> data + "-next");
        @SuppressWarnings("unchecked")
        var library = (Task<String>) library(task);
        var instance = new TaskInstance<>("platform.stateful-probe", "recurring", "first");
        @SuppressWarnings("unchecked")
        ExecutionOperations<String> operations = mock(ExecutionOperations.class);
        var complete = ExecutionComplete.success(new Execution(DONE, instance), DONE, DONE);

        // When
        library.execute(instance, null).complete(complete, operations);

        // Then
        verify(operations).reschedule(complete, DONE.plus(Duration.ofHours(1)), "first-next");
    }

    @Test
    void aOneTimeTaskHandsItsDataToTheAction() {
        // Given
        var received = new ArrayList<String>();
        var task = tasks.oneTime(TaskName.of("platform.one-time-probe"), String.class, received::add);
        @SuppressWarnings("unchecked")
        var library = (Task<String>) library(task);

        // When
        library.execute(new TaskInstance<>("platform.one-time-probe", "order-1", "payload"), null);

        // Then
        assertThat(received).containsExactly("payload");
    }

    @Test
    void aPerEntityTaskHandsTheEntityKeyToTheAction() {
        // Given
        var received = new ArrayList<String>();
        var task = tasks.perEntity(TaskName.of("platform.entity-probe"), received::add);
        @SuppressWarnings("unchecked")
        var library = (Task<StoredEntitySchedule>) library(task);
        var daily = new StoredEntitySchedule("0 0 4 * * *", ZoneId.of("UTC"));

        // When
        library.execute(new TaskInstance<>("platform.entity-probe", "branch-42", daily), null);

        // Then
        assertThat(received).containsExactly("branch-42");
    }

    @ParameterizedTest
    @ValueSource(ints = {0, 1, 2})
    void aFailingRecurringTaskRetriesWithExponentialBackoff(int earlierFailures) {
        // Given
        var task = tasks.recurring(TaskName.of("platform.probe"), HOURLY, () -> {});

        // When
        var retryAt = nextExecutionAfterFailure(library(task), null, earlierFailures);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF.multipliedBy(1L << earlierFailures)));
    }

    @Test
    void aRecurringTaskFallsBackToItsScheduleOnceItsRetriesAreUsedUp() {
        // Given
        var task = tasks.recurring(TaskName.of("platform.probe"), HOURLY, () -> {});

        // When
        var retryAt = nextExecutionAfterFailure(library(task), null, MAX_RETRIES);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(Duration.ofHours(1)));
    }

    @Test
    void aOneTimeTaskRetriesWithExponentialBackoff() {
        // Given
        var task = tasks.oneTime(TaskName.of("platform.one-time-probe"), String.class, data -> {});

        // When
        var retryAt = nextExecutionAfterFailure(library(task), "payload", 2);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF.multipliedBy(4)));
    }

    @Test
    void aPerEntityTaskRetriesWithBackoffThenFallsBackToTheEntitySchedule() {
        // Given
        var task = tasks.perEntity(TaskName.of("platform.entity-probe"), entityId -> {});
        var daily = new StoredEntitySchedule("0 0 4 * * *", ZoneId.of("UTC"));

        // When
        var retryAt = nextExecutionAfterFailure(library(task), daily, 0);
        var fallbackAt = nextExecutionAfterFailure(library(task), daily, MAX_RETRIES);

        // Then
        assertThat(retryAt).isEqualTo(DONE.plus(INITIAL_BACKOFF));
        assertThat(fallbackAt).isEqualTo(Instant.parse("2026-09-19T04:00:00Z"));
    }

    static Task<?> library(ScheduledTask task) {
        return ((LibraryTask) task).libraryTask();
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    static Instant nextExecutionAfterFailure(Task task, Object data, int earlierFailures) {
        var instance = new TaskInstance<>(task.getName(), "instance", data);
        var execution =
                new Execution(STARTED, instance, true, "instance-a", null, STARTED, earlierFailures, STARTED, 1);
        var failed = ExecutionComplete.failure(execution, STARTED, DONE, new IllegalStateException("boom"));
        ExecutionOperations operations = mock(ExecutionOperations.class);
        var next = ArgumentCaptor.forClass(Instant.class);

        task.getFailureHandler().onFailure(failed, operations);

        verify(operations).reschedule(eq(failed), next.capture());
        return next.getValue();
    }
}
