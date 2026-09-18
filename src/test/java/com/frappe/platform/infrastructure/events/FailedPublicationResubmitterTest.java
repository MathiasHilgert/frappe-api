package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatNoException;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import java.time.Duration;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataAccessResourceFailureException;
import org.springframework.modulith.events.FailedEventPublications;
import org.springframework.modulith.events.ResubmissionOptions;

class FailedPublicationResubmitterTest {

    final OutboxRecoveryProperties properties = new OutboxRecoveryProperties(Duration.ofSeconds(30), 50);

    final FailedEventPublications failed = mock(FailedEventPublications.class);

    final Logger logger = (Logger) LoggerFactory.getLogger(FailedPublicationResubmitter.class);

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
    void resubmitsOneBoundedBatchOfFailedPublications() {
        // Given
        var resubmitter = new FailedPublicationResubmitter(failed, properties);
        var options = ArgumentCaptor.forClass(ResubmissionOptions.class);

        // When
        resubmitter.run();

        // Then
        verify(failed).resubmit(options.capture());
        assertThat(options.getValue().getBatchSize()).isEqualTo(50);
        assertThat(options.getValue().getMaxInFlight()).isEqualTo(50);
        assertThat(options.getValue().getMinAge()).isZero();
    }

    @Test
    void databaseFailureIsLoggedOnceAndRetriedOnTheNextRun() {
        // Given
        var outage = new DataAccessResourceFailureException("connection refused");
        doThrow(outage).when(failed).resubmit(any());
        var resubmitter = new FailedPublicationResubmitter(failed, properties);

        // When / Then
        assertThatNoException().isThrownBy(resubmitter::run);
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("connection refused");
            assertThat(event.getKeyValuePairs())
                    .anySatisfy(pair -> assertThat(pair.key).isEqualTo("frappe.outbox.recovery_interval"));
        });
    }
}
