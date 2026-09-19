package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThatExceptionOfType;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import org.junit.jupiter.api.Test;
import org.springframework.dao.DataAccessResourceFailureException;

class OutboxArchivePurgerTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    final OutboxArchivePurgeProperties properties =
            new OutboxArchivePurgeProperties(Duration.ofDays(30), 2, Duration.ofHours(1));

    final OutboxArchivePurgeRepository repository = mock(OutboxArchivePurgeRepository.class);

    final Clock clock = Clock.fixed(NOW, ZoneOffset.UTC);

    final TestObservationRegistry observations = TestObservationRegistry.create();

    final OutboxArchivePurger purger = new OutboxArchivePurger(repository, properties, clock, observations);

    @Test
    void deletesArchivedRowsOlderThanTheRetentionThreshold() {
        // Given
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(0);

        // When
        purger.purge();

        // Then
        verify(repository).purgeArchivedBefore(Instant.parse("2026-08-19T12:00:00Z"), 2);
    }

    @Test
    void keepsDeletingArchivedRowsInBatchesUntilABatchIsNotFull() {
        // Given
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(2, 2, 1);

        // When
        purger.purge();

        // Then
        verify(repository, times(3)).purgeArchivedBefore(any(), eq(2));
    }

    @Test
    void keepsDeletingOrphanTraceContextRowsInBatchesUntilABatchIsNotFull() {
        // Given
        when(repository.purgeOrphanTraceContext(2)).thenReturn(2, 0);

        // When
        purger.purge();

        // Then
        verify(repository, times(2)).purgeOrphanTraceContext(2);
    }

    @Test
    void everyRunIsObservedWithThePurgedRowCounts() {
        // Given
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(2, 1);
        when(repository.purgeOrphanTraceContext(2)).thenReturn(1, 0);

        // When
        purger.purge();

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("outbox.purge")
                .hasHighCardinalityKeyValue("outbox.purge.archived.count", "3")
                .hasHighCardinalityKeyValue("outbox.purge.trace-context.count", "1")
                .hasBeenStopped();
    }

    @Test
    void aDatabaseFailureThrowsInsteadOfBeingCaughtAndLogged() {
        // Given
        var outage = new DataAccessResourceFailureException("connection refused");
        doThrow(outage).when(repository).purgeArchivedBefore(any(), eq(2));

        // When / Then
        assertThatExceptionOfType(DataAccessResourceFailureException.class)
                .isThrownBy(purger::purge)
                .isSameAs(outage);
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("outbox.purge")
                .hasError(outage)
                .hasBeenStopped();
    }
}
