package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import com.github.kagkarlsson.scheduler.task.StateReturningExecutionHandler;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import com.github.kagkarlsson.scheduler.task.helper.RecurringTask;
import com.github.kagkarlsson.scheduler.task.schedule.FixedDelay;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class OutboxRecoveryTaskTest {

    static final Duration INTERVAL = Duration.ofSeconds(30);

    final FailedPublicationResubmitter resubmitter = mock(FailedPublicationResubmitter.class);

    final ScheduledTasks tasks = mock(ScheduledTasks.class);

    @SuppressWarnings("unchecked")
    final ArgumentCaptor<StateReturningExecutionHandler<Trigger>> handler =
            ArgumentCaptor.forClass(StateReturningExecutionHandler.class);

    @Test
    @SuppressWarnings("unchecked")
    void isOneRecurringTaskOnAFixedDelayOfTheRecoveryIntervalStartingAsAScheduledPass() {
        // Given
        var declared = mock(RecurringTask.class);
        when(tasks.recurring(any(), any(), eq(Trigger.class), any(), any())).thenReturn(declared);

        // When
        var task = OutboxRecoveryTask.declare(tasks, resubmitter, INTERVAL);

        // Then
        assertThat(task).isSameAs(declared);
        verify(tasks)
                .recurring(
                        eq("platform.outbox-recovery"),
                        eq(FixedDelay.of(INTERVAL)),
                        eq(Trigger.class),
                        eq(Trigger.SCHEDULED),
                        any());
    }

    @Test
    void runsThePassForTheStoredTriggerAndMakesTheNextOneAScheduledPass() {
        // Given
        OutboxRecoveryTask.declare(tasks, resubmitter, INTERVAL);
        verify(tasks).recurring(any(), any(), eq(Trigger.class), any(), handler.capture());
        var instance =
                new TaskInstance<>("platform.outbox-recovery", RecurringTask.INSTANCE, Trigger.TRANSPORT_RECOVERED);

        // When
        var next = handler.getValue().execute(instance, null);

        // Then
        verify(resubmitter).recover(Trigger.TRANSPORT_RECOVERED);
        assertThat(next).isEqualTo(Trigger.SCHEDULED);
    }
}
