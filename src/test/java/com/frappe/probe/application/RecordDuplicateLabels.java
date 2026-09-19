package com.frappe.probe.application;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.Result;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Transactional;

/** Saves two probes with one label; the deferred unique constraint fails the commit, not the insert. */
@CommandUseCase
public class RecordDuplicateLabels {

    private final JdbcTemplate jdbc;
    private final Set<String> returnedLabels = ConcurrentHashMap.newKeySet();

    RecordDuplicateLabels(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @Transactional
    public Result<UUID, RecordProbe.ProbeError> record(UUID firstId, UUID secondId, String label) {
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
