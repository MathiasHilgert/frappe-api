package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.frappe.platform.RecurringTask;
import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import java.time.Duration;
import java.util.function.UnaryOperator;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class OutboxRecoveryTaskTest {

    static final Duration INTERVAL = Duration.ofSeconds(30);

    final FailedPublicationResubmitter resubmitter = mock(FailedPublicationResubmitter.class);

    final ScheduledTasks tasks = mock(ScheduledTasks.class);

    @SuppressWarnings("unchecked")
    final ArgumentCaptor<UnaryOperator<Trigger>> action = ArgumentCaptor.forClass(UnaryOperator.class);

    @Test
    @SuppressWarnings("unchecked")
    void isOneRecurringTaskOnAFixedDelayOfTheRecoveryIntervalStartingAsAScheduledPass() {
        // Given
        RecurringTask<Trigger> declared = mock(RecurringTask.class);
        when(tasks.recurring(any(), any(), eq(Trigger.class), any(), any())).thenReturn(declared);

        // When
        var task = OutboxRecoveryTask.declare(tasks, resubmitter, INTERVAL);

        // Then
        assertThat(task).isSameAs(declared);
        verify(tasks)
                .recurring(
                        eq(TaskName.of("platform.outbox-recovery")),
                        eq(TaskSchedule.fixedDelay(INTERVAL)),
                        eq(Trigger.class),
                        eq(Trigger.SCHEDULED),
                        any());
    }

    @Test
    void runsThePassForTheStoredTriggerAndMakesTheNextOneAScheduledPass() {
        // Given
        OutboxRecoveryTask.declare(tasks, resubmitter, INTERVAL);
        verify(tasks).recurring(any(), any(), eq(Trigger.class), any(), action.capture());

        // When
        var next = action.getValue().apply(Trigger.TRANSPORT_RECOVERED);

        // Then
        verify(resubmitter).recover(Trigger.TRANSPORT_RECOVERED);
        assertThat(next).isEqualTo(Trigger.SCHEDULED);
    }
}
