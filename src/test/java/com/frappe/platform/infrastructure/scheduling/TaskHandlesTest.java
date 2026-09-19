package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.argThat;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.frappe.platform.EntitySchedule;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import com.frappe.platform.TaskSchedulingException;
import com.github.kagkarlsson.scheduler.ScheduledExecution;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.SchedulerClient.ScheduleOptions;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceCurrentlyExecutingException;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceNotFoundException;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import com.github.kagkarlsson.scheduler.task.TaskInstanceId;
import com.github.kagkarlsson.shaded.jdbc.SQLRuntimeException;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.util.Optional;
import org.junit.jupiter.api.Test;

class TaskHandlesTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    static final TaskInstanceId RECOVERY = TaskInstanceId.of("platform.recovery-probe", "recurring");

    final Scheduler scheduler = mock(Scheduler.class);

    final ConventionalScheduledTasks tasks = new ConventionalScheduledTasks(
            new SchedulingProperties(Duration.ofSeconds(30), 3),
            () -> scheduler,
            Clock.fixed(NOW, ZoneOffset.UTC),
            new SimpleMeterRegistry());

    final com.frappe.platform.RecurringTask<String> recurring = tasks.recurring(
            TaskName.of("platform.recovery-probe"),
            TaskSchedule.fixedDelay(Duration.ofMinutes(1)),
            String.class,
            "scheduled",
            data -> "scheduled");

    final com.frappe.platform.OneTimeTask<String> oneTime =
            tasks.oneTime(TaskName.of("platform.reminder-probe"), String.class, data -> {});

    final com.frappe.platform.EntityTask perEntity = tasks.perEntity(TaskName.of("platform.close-probe"), id -> {});

    @Test
    void runningARecurringTaskNowMovesItsOneExecutionToNowAndWakesThePoller() {
        // Given
        when(scheduler.reschedule(RECOVERY, NOW, "urgent")).thenReturn(true);

        // When
        var moved = recurring.runNow("urgent");

        // Then
        assertThat(moved).isTrue();
        verify(scheduler).triggerCheckForDueExecutions();
    }

    @Test
    void aRecurringTaskThatIsRunningOrNotYetScheduledIsNotMoved() {
        // Given
        when(scheduler.reschedule(any(TaskInstanceId.class), any(), any()))
                .thenThrow(new TaskInstanceCurrentlyExecutingException("platform.recovery-probe", "recurring"))
                .thenThrow(new TaskInstanceNotFoundException("platform.recovery-probe", "recurring"))
                .thenReturn(false);

        // When / Then
        assertThat(recurring.runNow("urgent")).isFalse();
        assertThat(recurring.runNow("urgent")).isFalse();
        assertThat(recurring.runNow("urgent")).isFalse();
        verify(scheduler, never()).triggerCheckForDueExecutions();
    }

    @Test
    void aDatabaseFailureSurfacesAsATaskSchedulingException() {
        // Given
        when(scheduler.reschedule(any(TaskInstanceId.class), any(), any()))
                .thenThrow(new SQLRuntimeException("connection refused"));

        // When / Then
        assertThatExceptionOfType(TaskSchedulingException.class)
                .isThrownBy(() -> recurring.runNow("urgent"))
                .withMessageContaining("platform.recovery-probe")
                .withCauseInstanceOf(SQLRuntimeException.class);
    }

    @Test
    void schedulingAOneTimeTaskIsIdempotentByItsNaturalKey() {
        // Given
        when(scheduler.scheduleIfNotExists(any(TaskInstance.class), eq(NOW.plusSeconds(60))))
                .thenReturn(true)
                .thenReturn(false);

        // When / Then
        assertThat(oneTime.schedule("account-1", "payload", NOW.plusSeconds(60)))
                .isTrue();
        assertThat(oneTime.schedule("account-1", "payload", NOW.plusSeconds(60)))
                .isFalse();
        verify(scheduler, times(2))
                .scheduleIfNotExists(
                        eq(new TaskInstance<>("platform.reminder-probe", "account-1", "payload")),
                        eq(NOW.plusSeconds(60)));
    }

    @Test
    void anEntityScheduleIsCreatedOrReplacedAtItsNextCronTime() {
        // Given
        when(scheduler.schedule(any(TaskInstance.class), any(Instant.class), any(ScheduleOptions.class)))
                .thenReturn(true);

        // When
        perEntity.schedule("branch-1", new EntitySchedule("0 0 4 * * *", ZoneId.of("America/Sao_Paulo")));

        // Then
        verify(scheduler)
                .schedule(
                        argThat((TaskInstance<?> instance) ->
                                instance.getTaskName().equals("platform.close-probe")
                                        && instance.getId().equals("branch-1")
                                        && instance.getData()
                                                .equals(new StoredEntitySchedule(
                                                        "0 0 4 * * *", ZoneId.of("America/Sao_Paulo")))),
                        eq(Instant.parse("2026-09-19T07:00:00Z")),
                        eq(ScheduleOptions.WHEN_EXISTS_RESCHEDULE));
    }

    @Test
    void anEntityScheduleChangedConcurrentlyAsksForARetry() {
        // Given another instance removed or rescheduled the execution between insert and reschedule
        when(scheduler.schedule(any(TaskInstance.class), any(Instant.class), any(ScheduleOptions.class)))
                .thenReturn(false);

        // When / Then
        assertThatExceptionOfType(TaskSchedulingException.class)
                .isThrownBy(() -> perEntity.schedule("branch-1", new EntitySchedule("0 0 4 * * *", ZoneId.of("UTC"))))
                .withMessageContaining("branch-1")
                .withMessageContaining("changed concurrently");
    }

    @Test
    void anInvalidEntityCronIsRejectedWhenScheduled() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> perEntity.schedule("branch-1", new EntitySchedule("every day", ZoneId.of("UTC"))))
                .withMessageContaining("every day");
    }

    @Test
    void anEntityScheduleCannotChangeWhileItsExecutionRuns() {
        // Given
        when(scheduler.schedule(any(TaskInstance.class), any(Instant.class), any(ScheduleOptions.class)))
                .thenThrow(new TaskInstanceCurrentlyExecutingException("platform.close-probe", "branch-1"));

        // When / Then
        assertThatExceptionOfType(TaskSchedulingException.class)
                .isThrownBy(() -> perEntity.schedule("branch-1", new EntitySchedule("0 0 4 * * *", ZoneId.of("UTC"))))
                .withMessageContaining("branch-1");
    }

    @Test
    void cancellingAnEntityWithoutScheduleReportsFalse() {
        // Given
        doThrow(new TaskInstanceNotFoundException("platform.close-probe", "branch-9"))
                .when(scheduler)
                .cancel(TaskInstanceId.of("platform.close-probe", "branch-9"));

        // When / Then
        assertThat(perEntity.cancel("branch-9")).isFalse();
        assertThat(perEntity.cancel("branch-1")).isTrue();
    }

    @Test
    @SuppressWarnings({"unchecked", "rawtypes"})
    void readsTheNextRunOfAnEntity() {
        // Given
        var execution = mock(ScheduledExecution.class);
        when(execution.getExecutionTime()).thenReturn(NOW.plusSeconds(3600));
        when(scheduler.getScheduledExecution(TaskInstanceId.of("platform.close-probe", "branch-1")))
                .thenReturn((Optional) Optional.of(execution));

        // When / Then
        assertThat(perEntity.nextRun("branch-1")).contains(NOW.plusSeconds(3600));
        assertThat(perEntity.nextRun("branch-2")).isEmpty();
    }
}
