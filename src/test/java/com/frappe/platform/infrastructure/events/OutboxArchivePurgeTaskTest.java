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
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class OutboxArchivePurgeTaskTest {

    static final Duration INTERVAL = Duration.ofHours(1);

    final OutboxArchivePurger purger = mock(OutboxArchivePurger.class);

    final ScheduledTasks tasks = mock(ScheduledTasks.class);

    @SuppressWarnings("unchecked")
    final ArgumentCaptor<Runnable> action = ArgumentCaptor.forClass(Runnable.class);

    @Test
    @SuppressWarnings("unchecked")
    void isOneRecurringTaskOnAFixedDelayOfThePurgeInterval() {
        // Given
        RecurringTask<Void> declared = mock(RecurringTask.class);
        when(tasks.recurring(any(), any(), any(Runnable.class))).thenReturn(declared);

        // When
        var task = OutboxArchivePurgeTask.declare(tasks, purger, INTERVAL);

        // Then
        assertThat(task).isSameAs(declared);
        verify(tasks)
                .recurring(
                        eq(TaskName.of("platform.purge-event-archive")), eq(TaskSchedule.fixedDelay(INTERVAL)), any());
    }

    @Test
    void runningTheTaskRunsThePurge() {
        // Given
        OutboxArchivePurgeTask.declare(tasks, purger, INTERVAL);
        verify(tasks).recurring(any(), any(), action.capture());

        // When
        action.getValue().run();

        // Then
        verify(purger).purge();
    }
}
