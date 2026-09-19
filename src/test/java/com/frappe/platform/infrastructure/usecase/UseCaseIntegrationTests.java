package com.frappe.platform.infrastructure.usecase;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.assertj.core.api.Assertions.catchRuntimeException;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.CommandUseCase;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.Result;
import com.frappe.platform.infrastructure.ids.TestIds;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.events.Externalized;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.annotation.Transactional;

/**
 * Use cases over real Postgres: state and outbox rows share the use case's transaction, a returned failure rolls both
 * back, and the transaction (commit included) runs inside the {@code use_case} observation.
 */
@SpringBootTest
@Import({
    TestcontainersConfiguration.class,
    TestNatsConfiguration.class,
    UseCaseIntegrationTests.RecordProbe.class,
    UseCaseIntegrationTests.RecordDuplicateLabels.class
})
@ActiveProfiles("local")
class UseCaseIntegrationTests {

    private static final Clock CLOCK = Clock.fixed(Instant.parse("2026-09-19T12:00:00Z"), ZoneOffset.UTC);

    enum ProbeError {
        REJECTED
    }

    /** How {@link RecordProbe} ends after saving and recording its event. */
    enum Ending {
        SUCCESS,
        FAILURE,
        DEFECT
    }

    @Externalized
    record ProbeRecorded(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    /** Saves a probe row and records {@link ProbeRecorded}, then ends as told. */
    @CommandUseCase
    static class RecordProbe {

        static final IllegalStateException DEFECT = new IllegalStateException("defect after saving");

        private final JdbcTemplate jdbc;
        private final DomainEventPublisher events;

        RecordProbe(JdbcTemplate jdbc, DomainEventPublisher events) {
            this.jdbc = jdbc;
            this.events = events;
        }

        @Transactional
        public Result<UUID, ProbeError> record(UUID probeId, UUID eventId, Ending ending) {
            jdbc.update("insert into fixture.probe (id, label) values (?, ?)", probeId, "use case");
            events.publish(new ProbeRecorded(eventId, CLOCK.instant(), probeId, 1, 1));
            return switch (ending) {
                case SUCCESS -> Result.success(probeId);
                case FAILURE -> Result.failure(ProbeError.REJECTED);
                case DEFECT -> throw DEFECT;
            };
        }
    }

    /** Saves two probes with one label; the deferred unique constraint fails the commit, not the insert. */
    @CommandUseCase
    static class RecordDuplicateLabels {

        private final JdbcTemplate jdbc;
        private final Set<String> returnedLabels = ConcurrentHashMap.newKeySet();

        RecordDuplicateLabels(JdbcTemplate jdbc) {
            this.jdbc = jdbc;
        }

        @Transactional
        public Result<UUID, ProbeError> record(UUID firstId, UUID secondId, String label) {
            var insert = "insert into fixture.labelled_probe (id, label) values (?, ?)";
            jdbc.update(insert, firstId, label);
            jdbc.update(insert, secondId, label);
            returnedLabels.add(label);
            return Result.success(firstId);
        }

        /** Whether the method returned for the label: then a later failure can only come from the commit. */
        boolean returnedFor(String label) {
            return returnedLabels.contains(label);
        }
    }

    private final IdGenerator ids = TestIds.withClock(CLOCK);

    @Autowired
    RecordProbe recordProbe;

    @Autowired
    RecordDuplicateLabels recordDuplicateLabels;

    @Autowired
    JdbcTemplate jdbc;

    @Autowired
    MeterRegistry meters;

    @Test
    void aCommittedCommandStoresItsStateAndItsOutboxRow() {
        // Given
        var probeId = ids.newId();
        var eventId = ids.newId();

        // When
        var result = recordProbe.record(probeId, eventId, Ending.SUCCESS);

        // Then
        assertThat(result).isEqualTo(Result.success(probeId));
        assertThat(probeRows(probeId)).isOne();
        assertThat(outboxRows(eventId)).isOne();
        assertThat(useCaseTimerCount("RecordProbe", "success", "none")).isOne();
    }

    @Test
    void aCommandReturningAFailureRollsBackItsStateAndItsOutboxRow() {
        // Given
        var probeId = ids.newId();
        var eventId = ids.newId();

        // When
        var result = recordProbe.record(probeId, eventId, Ending.FAILURE);

        // Then
        assertThat(result).isEqualTo(Result.failure(ProbeError.REJECTED));
        assertThat(probeRows(probeId)).isZero();
        assertThat(outboxRows(eventId)).isZero();
        assertThat(useCaseTimerCount("RecordProbe", "failure", "none")).isOne();
    }

    @Test
    void aThrowingCommandLeavesNeitherItsStateNorItsOutboxRow() {
        // Given
        var probeId = ids.newId();
        var eventId = ids.newId();

        // When
        assertThatThrownBy(() -> recordProbe.record(probeId, eventId, Ending.DEFECT))
                .isSameAs(RecordProbe.DEFECT);

        // Then
        assertThat(probeRows(probeId)).isZero();
        assertThat(outboxRows(eventId)).isZero();
        assertThat(useCaseTimerCount("RecordProbe", "error", "IllegalStateException"))
                .isOne();
    }

    @Test
    void aCommandFailingOnCommitIsObservedAsAnError() {
        // Given
        var label = "label-" + ids.newId();

        // When
        var failure = catchRuntimeException(() -> recordDuplicateLabels.record(ids.newId(), ids.newId(), label));

        // Then
        assertThat(failure).isNotNull();
        assertThat(recordDuplicateLabels.returnedFor(label)).isTrue();
        assertThat(meters.find("use_case")
                        .tag("use_case.name", "RecordDuplicateLabels")
                        .tag("use_case.module", "platform")
                        .tag("use_case.kind", "command")
                        .tag("outcome", "error")
                        .timers())
                .singleElement()
                .satisfies(timer -> {
                    assertThat(timer.count()).isOne();
                    assertThat(timer.getId().getTag("error"))
                            .isEqualTo(failure.getClass().getSimpleName());
                });
    }

    private long useCaseTimerCount(String useCase, String outcome, String error) {
        var timer = meters.find("use_case")
                .tag("use_case.name", useCase)
                .tag("outcome", outcome)
                .tag("error", error)
                .timer();
        return timer == null ? 0 : timer.count();
    }

    private int probeRows(UUID probeId) {
        var rows = jdbc.queryForObject("select count(*) from fixture.probe where id = ?", Integer.class, probeId);
        return rows == null ? 0 : rows;
    }

    // Counts the outbox and its archive: the relay may already have completed and archived the publication.
    private int outboxRows(UUID eventId) {
        var rows = jdbc.queryForObject(
                "select (select count(*) from platform.event_publication where serialized_event like ?)"
                        + " + (select count(*) from platform.event_publication_archive where serialized_event like ?)",
                Integer.class,
                "%" + eventId + "%",
                "%" + eventId + "%");
        return rows == null ? 0 : rows;
    }
}
