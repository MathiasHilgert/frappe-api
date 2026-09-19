package com.frappe.platform.infrastructure.bus;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.assertj.core.api.Assertions.catchRuntimeException;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.Command;
import com.frappe.platform.CommandBus;
import com.frappe.platform.CommandHandler;
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
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.events.Externalized;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.annotation.Transactional;

/**
 * The bus over real Postgres: a command handler's state and outbox rows share its transaction, and the transaction
 * (commit included) runs inside the {@code use_case} observation.
 */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class UseCaseBusIntegrationTests {

    private static final Clock CLOCK = Clock.fixed(Instant.parse("2026-09-19T12:00:00Z"), ZoneOffset.UTC);

    enum ProbeError {
        REJECTED
    }

    /** How {@link RecordProbeHandler} ends after saving and recording its event. */
    enum Ending {
        SUCCESS,
        FAILURE,
        DEFECT
    }

    /** Saves a probe row and records {@link ProbeRecorded}, then ends as told. */
    record RecordProbe(UUID probeId, UUID eventId, Ending ending) implements Command<Result<UUID, ProbeError>> {}

    /** Saves two probes with one label; the deferred unique constraint fails the commit, not the insert. */
    record RecordDuplicateLabels(UUID firstId, UUID secondId, String label)
            implements Command<Result<UUID, ProbeError>> {}

    @Externalized
    record ProbeRecorded(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    static class RecordProbeHandler implements CommandHandler<RecordProbe, Result<UUID, ProbeError>> {

        static final IllegalStateException DEFECT = new IllegalStateException("defect after saving");

        private final JdbcTemplate jdbc;
        private final DomainEventPublisher events;

        RecordProbeHandler(JdbcTemplate jdbc, DomainEventPublisher events) {
            this.jdbc = jdbc;
            this.events = events;
        }

        @Override
        @Transactional
        public Result<UUID, ProbeError> handle(RecordProbe command) {
            jdbc.update("insert into fixture.probe (id, label) values (?, ?)", command.probeId(), "bus");
            events.publish(new ProbeRecorded(command.eventId(), CLOCK.instant(), command.probeId(), 1, 1));
            return switch (command.ending()) {
                case SUCCESS -> Result.success(command.probeId());
                case FAILURE -> Result.failure(ProbeError.REJECTED);
                case DEFECT -> throw DEFECT;
            };
        }
    }

    static class RecordDuplicateLabelsHandler
            implements CommandHandler<RecordDuplicateLabels, Result<UUID, ProbeError>> {

        private final JdbcTemplate jdbc;
        private final Set<String> returnedLabels = ConcurrentHashMap.newKeySet();

        RecordDuplicateLabelsHandler(JdbcTemplate jdbc) {
            this.jdbc = jdbc;
        }

        /** Whether handle returned for the label: then a later failure can only come from the commit. */
        public boolean returnedFor(String label) {
            return returnedLabels.contains(label);
        }

        @Override
        @Transactional
        public Result<UUID, ProbeError> handle(RecordDuplicateLabels command) {
            var insert = "insert into fixture.labelled_probe (id, label) values (?, ?)";
            jdbc.update(insert, command.firstId(), command.label());
            jdbc.update(insert, command.secondId(), command.label());
            returnedLabels.add(command.label());
            return Result.success(command.firstId());
        }
    }

    /** Handlers as beans of this test only; component-scanned classes would leak into every context. */
    @TestConfiguration(proxyBeanMethods = false)
    static class Handlers {

        @Bean
        RecordProbeHandler recordProbeHandler(JdbcTemplate jdbc, DomainEventPublisher events) {
            return new RecordProbeHandler(jdbc, events);
        }

        @Bean
        RecordDuplicateLabelsHandler recordDuplicateLabelsHandler(JdbcTemplate jdbc) {
            return new RecordDuplicateLabelsHandler(jdbc);
        }
    }

    private final IdGenerator ids = TestIds.withClock(CLOCK);

    @Autowired
    CommandBus bus;

    @Autowired
    JdbcTemplate jdbc;

    @Autowired
    MeterRegistry meters;

    @Autowired
    RecordDuplicateLabelsHandler duplicateLabelsHandler;

    @Test
    void aCommittedCommandStoresItsStateAndItsOutboxRow() {
        // Given
        var command = new RecordProbe(ids.newId(), ids.newId(), Ending.SUCCESS);

        // When
        var result = bus.dispatch(command);

        // Then
        assertThat(result).isEqualTo(Result.success(command.probeId()));
        assertThat(probeRows(command.probeId())).isOne();
        assertThat(outboxRows(command.eventId())).isOne();
    }

    @Test
    void aRolledBackCommandLeavesNeitherItsStateNorItsOutboxRow() {
        // Given
        var command = new RecordProbe(ids.newId(), ids.newId(), Ending.DEFECT);

        // When
        assertThatThrownBy(() -> bus.dispatch(command)).isSameAs(RecordProbeHandler.DEFECT);

        // Then
        assertThat(probeRows(command.probeId())).isZero();
        assertThat(outboxRows(command.eventId())).isZero();
        assertThat(useCaseTimerCount("RecordProbe", "error", "IllegalStateException"))
                .isOne();
    }

    @Test
    void aCommandReturningAFailureRollsBackItsStateAndItsOutboxRow() {
        // Given
        var command = new RecordProbe(ids.newId(), ids.newId(), Ending.FAILURE);

        // When
        var result = bus.dispatch(command);

        // Then
        assertThat(result).isEqualTo(Result.failure(ProbeError.REJECTED));
        assertThat(probeRows(command.probeId())).isZero();
        assertThat(outboxRows(command.eventId())).isZero();
        assertThat(useCaseTimerCount("RecordProbe", "failure", "none")).isOne();
    }

    @Test
    void aCommandFailingOnCommitIsObservedAsAnError() {
        // Given
        var command = new RecordDuplicateLabels(ids.newId(), ids.newId(), "label-" + ids.newId());

        // When
        var failure = catchRuntimeException(() -> bus.dispatch(command));

        // Then
        assertThat(failure).isNotNull();
        assertThat(duplicateLabelsHandler.returnedFor(command.label())).isTrue();
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
        assertThat(meters.find("use_case")
                        .tag("use_case.name", "RecordDuplicateLabels")
                        .tag("outcome", "success")
                        .timer())
                .isNull();
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
