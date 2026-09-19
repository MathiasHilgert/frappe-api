package com.frappe.probe.application;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.Result;
import com.frappe.probe.ProbeRecorded;
import java.time.Clock;
import java.util.UUID;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Transactional;

/**
 * Saves a probe row and records {@link ProbeRecorded}, then ends as told. Test module code: it is also registered in
 * every other full test context (the use case scan covers {@code com.frappe}), where its dependencies exist too.
 */
@CommandUseCase
public class RecordProbe {

    /** How the command ends after saving and recording its event. */
    public enum Ending {
        SUCCESS,
        FAILURE,
        DEFECT
    }

    /** The only expected failure. */
    public enum ProbeError {
        REJECTED
    }

    /** Thrown for {@link Ending#DEFECT}. */
    public static final IllegalStateException DEFECT = new IllegalStateException("defect after saving");

    private final JdbcTemplate jdbc;
    private final DomainEventPublisher events;
    private final Clock clock;

    RecordProbe(JdbcTemplate jdbc, DomainEventPublisher events, Clock clock) {
        this.jdbc = jdbc;
        this.events = events;
        this.clock = clock;
    }

    @Transactional
    public Result<UUID, ProbeError> record(UUID probeId, UUID eventId, Ending ending) {
        jdbc.update("insert into fixture.probe (id, label) values (?, ?)", probeId, "use case");
        events.publish(new ProbeRecorded(eventId, clock.instant(), probeId, 1, 1));
        return switch (ending) {
            case SUCCESS -> Result.success(probeId);
            case FAILURE -> Result.failure(ProbeError.REJECTED);
            case DEFECT -> throw DEFECT;
        };
    }
}
