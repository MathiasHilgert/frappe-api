package com.frappe.probe.application;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.assertj.core.api.Assertions.catchRuntimeException;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.Result;
import com.frappe.probe.application.RecordProbe.Ending;
import com.frappe.probe.application.RecordProbe.ProbeError;
import io.micrometer.core.instrument.MeterRegistry;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.test.ApplicationModuleTest;
import org.springframework.test.context.ActiveProfiles;

/**
 * A module bootstrapped alone (STANDALONE, the default) still gets its use cases registered, observed and rolled back:
 * {@code platform} is a shared module, so its use case support loads with every module. Runs on real Postgres.
 */
@ApplicationModuleTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class ProbeModuleTests {

    private final RecordProbe recordProbe;
    private final RecordDuplicateLabels recordDuplicateLabels;
    private final CountProbes countProbes;
    private final IdGenerator ids;
    private final JdbcTemplate jdbc;
    private final MeterRegistry meters;

    // Constructor injection: the test class belongs to the probe module, which Modulith verifies like any module.
    @Autowired
    ProbeModuleTests(
            RecordProbe recordProbe,
            RecordDuplicateLabels recordDuplicateLabels,
            CountProbes countProbes,
            IdGenerator ids,
            JdbcTemplate jdbc,
            MeterRegistry meters) {
        this.recordProbe = recordProbe;
        this.recordDuplicateLabels = recordDuplicateLabels;
        this.countProbes = countProbes;
        this.ids = ids;
        this.jdbc = jdbc;
        this.meters = meters;
    }

    @Test
    void aCommittedCommandStoresItsStateAndItsOutboxRowAndIsObserved() {
        // Given
        var probeId = ids.newId();
        var eventId = ids.newId();

        // When
        var result = recordProbe.record(probeId, eventId, Ending.SUCCESS);

        // Then
        assertThat(result).isEqualTo(Result.success(probeId));
        assertThat(countProbes.count(probeId)).isOne();
        assertThat(outboxRows(eventId)).isOne();
        assertThat(useCaseTimerCount("RecordProbe", "command", "success", "none"))
                .isPositive();
        assertThat(useCaseTimerCount("CountProbes", "query", "success", "none")).isPositive();
    }

    @Test
    void aCommandReturningAFailureRollsBackItsStateAndItsOutboxRow() {
        // Given
        var probeId = ids.newId();
        var eventId = ids.newId();
        var failuresBefore = useCaseTimerCount("RecordProbe", "command", "failure", "none");

        // When
        var result = recordProbe.record(probeId, eventId, Ending.FAILURE);

        // Then
        assertThat(result).isEqualTo(Result.failure(ProbeError.REJECTED));
        assertThat(countProbes.count(probeId)).isZero();
        assertThat(outboxRows(eventId)).isZero();
        assertThat(useCaseTimerCount("RecordProbe", "command", "failure", "none"))
                .isEqualTo(failuresBefore + 1);
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
        assertThat(countProbes.count(probeId)).isZero();
        assertThat(outboxRows(eventId)).isZero();
        assertThat(useCaseTimerCount("RecordProbe", "command", "error", "IllegalStateException"))
                .isPositive();
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
        assertThat(useCaseTimerCount(
                        "RecordDuplicateLabels",
                        "command",
                        "error",
                        failure.getClass().getSimpleName()))
                .isOne();
    }

    private long useCaseTimerCount(String useCase, String kind, String outcome, String error) {
        var timer = meters.find("use_case")
                .tag("use_case.name", useCase)
                .tag("use_case.module", "probe")
                .tag("use_case.kind", kind)
                .tag("outcome", outcome)
                .tag("error", error)
                .timer();
        return timer == null ? 0 : timer.count();
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
