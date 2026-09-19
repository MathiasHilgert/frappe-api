package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatNoException;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.platform.RecurringTask;
import com.frappe.platform.TaskSchedulingException;
import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import java.util.ArrayList;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;

class OutboxRecoveryTriggerTest {

    @SuppressWarnings("unchecked")
    final RecurringTask<Trigger> recovery = mock(RecurringTask.class);

    final OutboxRecoveryTrigger trigger = new OutboxRecoveryTrigger(recovery, Runnable::run);

    final Logger logger = (Logger) LoggerFactory.getLogger(OutboxRecoveryTrigger.class);

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
    void aRecoveredTransportRunsTheRecoveryNowIgnoringTheBackoff() {
        // Given
        when(recovery.runNow(Trigger.TRANSPORT_RECOVERED)).thenReturn(true);

        // When
        trigger.onTransportRecovered(MessagingTransportRecovered.NATS);

        // Then
        verify(recovery).runNow(Trigger.TRANSPORT_RECOVERED);
        assertThat(logs.list).noneMatch(event -> event.getLevel().isGreaterOrEqual(Level.WARN));
    }

    @Test
    void theTransportThreadOnlyHandsTheRescheduleToTheExecutor() {
        // Given
        var handedOff = new ArrayList<Runnable>();
        var deferred = new OutboxRecoveryTrigger(recovery, handedOff::add);

        // When
        deferred.onTransportRecovered(MessagingTransportRecovered.NATS);

        // Then
        verify(recovery, never()).runNow(any());
        assertThat(handedOff).singleElement();
        handedOff.getFirst().run();
        verify(recovery).runNow(Trigger.TRANSPORT_RECOVERED);
    }

    @Test
    void aPassThatCouldNotBeMovedIsLeftToTheRunningOrScheduledOne() {
        // Given
        when(recovery.runNow(Trigger.TRANSPORT_RECOVERED)).thenReturn(false);

        // When / Then
        assertThatNoException().isThrownBy(() -> trigger.onTransportRecovered(MessagingTransportRecovered.NATS));
        assertThat(logs.list).noneMatch(event -> event.getLevel().isGreaterOrEqual(Level.WARN));
    }

    @Test
    void aDatabaseFailureIsLoggedOnceAndLeftToTheScheduledRun() {
        // Given
        when(recovery.runNow(Trigger.TRANSPORT_RECOVERED))
                .thenThrow(new TaskSchedulingException(
                        "Moving platform.outbox-recovery failed", new RuntimeException("x")));

        // When / Then
        assertThatNoException().isThrownBy(() -> trigger.onTransportRecovered(MessagingTransportRecovered.NATS));
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("Moving platform.outbox-recovery failed");
        });
    }
}
