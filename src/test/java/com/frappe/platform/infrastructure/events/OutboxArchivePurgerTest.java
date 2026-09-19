package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThatCode;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyCollection;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.dao.DataAccessResourceFailureException;

class OutboxArchivePurgerTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    static final Instant THRESHOLD = Instant.parse("2026-08-19T12:00:00Z");

    final OutboxArchivePurgeProperties properties =
            new OutboxArchivePurgeProperties(Duration.ofDays(30), 2, Duration.ofHours(1));

    final OutboxArchivePurgeRepository repository = mock(OutboxArchivePurgeRepository.class);

    final Clock clock = Clock.fixed(NOW, ZoneOffset.UTC);

    final TestObservationRegistry observations = TestObservationRegistry.create();

    final OutboxArchivePurger purger = new OutboxArchivePurger(repository, properties, clock, observations);

    @Test
    void deletesArchivedRowsOlderThanTheRetentionThreshold() {
        // Given
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(List.of());

        // When
        purger.purge();

        // Then
        verify(repository).purgeArchivedBefore(THRESHOLD, 2);
    }

    @Test
    void keepsDeletingArchivedRowsInBatchesUntilABatchIsNotFull() {
        // Given
        var first = List.of(UUID.randomUUID(), UUID.randomUUID());
        var second = List.of(UUID.randomUUID(), UUID.randomUUID());
        var third = List.of(UUID.randomUUID());
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(first, second, third);

        // When
        purger.purge();

        // Then
        verify(repository, times(3)).purgeArchivedBefore(any(), eq(2));
    }

    @Test
    void purgesTraceContextOfEveryArchivedBatchByIndexedEventId() {
        // Given
        var first = List.of(UUID.randomUUID(), UUID.randomUUID());
        var second = List.of(UUID.randomUUID());
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(first, second);
        when(repository.purgeOrphanTraceContext(THRESHOLD, 2)).thenReturn(0);

        // When
        purger.purge();

        // Then
        verify(repository).purgeTraceContextFor(Set.copyOf(first));
        verify(repository).purgeTraceContextFor(Set.copyOf(second));
    }

    @Test
    void anEmptyArchivedBatchSkipsTheTraceContextLookup() {
        // Given
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(List.of());

        // When
        purger.purge();

        // Then
        verify(repository, never()).purgeTraceContextFor(anyCollection());
    }

    // safe_event_id (V202609191930) returns null for a row it cannot parse into a UUID; Set.copyOf used to reject
    // that null outright (NullPointerException), which would fail the whole run over one malformed archived row.
    @Test
    void aNullEventIdInTheArchivedBatchIsFilteredOutInsteadOfBreakingTheRun() {
        // Given
        var readable = UUID.randomUUID();
        var malformed = new ArrayList<UUID>(Arrays.asList(readable, null));
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(malformed);
        when(repository.purgeOrphanTraceContext(THRESHOLD, 2)).thenReturn(0);

        // When
        assertThatCode(purger::purge).doesNotThrowAnyException();

        // Then: only the readable event id is looked up, the batch size (2) still ends the loop correctly.
        verify(repository).purgeTraceContextFor(Set.of(readable));
        verify(repository).purgeArchivedBefore(any(), eq(2));
    }

    @Test
    void anArchivedBatchOfOnlyNullEventIdsSkipsTheTraceContextLookup() {
        // Given
        var onlyMalformed = new ArrayList<UUID>();
        onlyMalformed.add(null);
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(onlyMalformed, List.of());
        when(repository.purgeOrphanTraceContext(THRESHOLD, 2)).thenReturn(0);

        // When
        assertThatCode(purger::purge).doesNotThrowAnyException();

        // Then
        verify(repository, never()).purgeTraceContextFor(anyCollection());
    }

    @Test
    void keepsDeletingOrphanTraceContextRowsInBatchesUntilABatchIsNotFull() {
        // Given
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(List.of());
        when(repository.purgeOrphanTraceContext(THRESHOLD, 2)).thenReturn(2, 0);

        // When
        purger.purge();

        // Then
        verify(repository, times(2)).purgeOrphanTraceContext(THRESHOLD, 2);
    }

    @Test
    void everyRunIsObservedWithThePurgedRowCounts() {
        // Given
        var first = List.of(UUID.randomUUID(), UUID.randomUUID());
        var second = List.of(UUID.randomUUID());
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(first, second);
        when(repository.purgeTraceContextFor(any())).thenReturn(1, 0);
        when(repository.purgeOrphanTraceContext(THRESHOLD, 2)).thenReturn(1, 0);

        // When
        purger.purge();

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("outbox.purge")
                .hasHighCardinalityKeyValue("outbox.purge.archived.count", "3")
                .hasHighCardinalityKeyValue("outbox.purge.trace_context.count", "2")
                .hasBeenStopped();
    }

    @Test
    void aDatabaseFailureThrowsInsteadOfBeingCaughtAndLoggedButStillRecordsThePurgedCountsSoFar() {
        // Given
        var first = List.of(UUID.randomUUID());
        when(repository.purgeArchivedBefore(any(), eq(2))).thenReturn(first);
        when(repository.purgeTraceContextFor(any())).thenReturn(1);
        var outage = new DataAccessResourceFailureException("connection refused");
        doThrow(outage).when(repository).purgeOrphanTraceContext(THRESHOLD, 2);

        // When / Then
        assertThatExceptionOfType(DataAccessResourceFailureException.class)
                .isThrownBy(purger::purge)
                .isSameAs(outage);
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("outbox.purge")
                .hasError(outage)
                .hasHighCardinalityKeyValue("outbox.purge.archived.count", "1")
                .hasHighCardinalityKeyValue("outbox.purge.trace_context.count", "1")
                .hasBeenStopped();
    }
}
