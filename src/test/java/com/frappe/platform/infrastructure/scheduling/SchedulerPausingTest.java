package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.SchedulerState;
import org.junit.jupiter.api.Test;

class SchedulerPausingTest {

    final Scheduler scheduler = mock(Scheduler.class);

    final SchedulerState state = mock(SchedulerState.class);

    final SchedulerPausing pausing = new SchedulerPausing(scheduler);

    @Test
    void stoppingTheContextStopsPickingNewExecutions() {
        // When
        pausing.stop();

        // Then
        verify(scheduler).pause();
    }

    @Test
    void startingTheContextResumesPicking() {
        // When
        pausing.start();

        // Then
        verify(scheduler).resume();
    }

    @Test
    void runsWhileTheSchedulerIsNotPaused() {
        // Given
        when(scheduler.getSchedulerState()).thenReturn(state);
        when(state.isPaused()).thenReturn(false);

        // When / Then
        assertThat(pausing.isRunning()).isTrue();
    }

    @Test
    void stopsFirstOnShutdownSoNoNewWorkStartsWhileTheRestShutsDown() {
        assertThat(pausing.getPhase()).isEqualTo(Integer.MAX_VALUE);
    }
}
