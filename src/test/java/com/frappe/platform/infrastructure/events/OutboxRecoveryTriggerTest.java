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
import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceCurrentlyExecutingException;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceNotFoundException;
import com.github.kagkarlsson.scheduler.task.TaskInstanceId;
import com.github.kagkarlsson.shaded.jdbc.SQLRuntimeException;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;

class OutboxRecoveryTriggerTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    static final TaskInstanceId RECOVERY = TaskInstanceId.of("platform.outbox-recovery", "recurring");

    final Scheduler scheduler = mock(Scheduler.class);

    final OutboxRecoveryTrigger trigger = new OutboxRecoveryTrigger(scheduler, Clock.fixed(NOW, ZoneOffset.UTC));

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
    void aRecoveredTransportMovesTheOneRecoveryExecutionToNowAndWakesThePoller() {
        // When
        trigger.onTransportRecovered(MessagingTransportRecovered.NATS);

        // Then
        verify(scheduler).reschedule(RECOVERY, NOW, Trigger.TRANSPORT_RECOVERED);
        verify(scheduler).triggerCheckForDueExecutions();
    }

    @Test
    void aRunningPassMakesTheTriggerRedundantBecauseItCoversTheSameRows() {
        // Given
        when(scheduler.reschedule(any(TaskInstanceId.class), any(), any()))
                .thenThrow(new TaskInstanceCurrentlyExecutingException("platform.outbox-recovery", "recurring"));

        // When / Then
        assertThatNoException().isThrownBy(() -> trigger.onTransportRecovered(MessagingTransportRecovered.NATS));
        verify(scheduler, never()).triggerCheckForDueExecutions();
        assertThat(logs.list).noneMatch(event -> event.getLevel().isGreaterOrEqual(Level.WARN));
    }

    @Test
    void aTriggerBeforeTheFirstScheduleIsLeftToTheStartupRun() {
        // Given
        when(scheduler.reschedule(any(TaskInstanceId.class), any(), any()))
                .thenThrow(new TaskInstanceNotFoundException("platform.outbox-recovery", "recurring"));

        // When / Then
        assertThatNoException().isThrownBy(() -> trigger.onTransportRecovered(MessagingTransportRecovered.NATS));
        assertThat(logs.list).noneMatch(event -> event.getLevel().isGreaterOrEqual(Level.WARN));
    }

    @Test
    void aDatabaseFailureIsLoggedOnceAndLeftToTheScheduledRun() {
        // Given
        when(scheduler.reschedule(any(TaskInstanceId.class), any(), any()))
                .thenThrow(new SQLRuntimeException("connection refused"));

        // When / Then
        assertThatNoException().isThrownBy(() -> trigger.onTransportRecovered(MessagingTransportRecovered.NATS));
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("connection refused");
        });
    }
}
