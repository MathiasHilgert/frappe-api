package com.frappe.probe.application;

import com.frappe.platform.QueryUseCase;
import java.util.UUID;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Transactional;

/** Counts probe rows with an id. */
@QueryUseCase
public class CountProbes {

    private final JdbcTemplate jdbc;

    CountProbes(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @Transactional(readOnly = true)
    public int count(UUID probeId) {
        var rows = jdbc.queryForObject("select count(*) from fixture.probe where id = ?", Integer.class, probeId);
        return rows == null ? 0 : rows;
    }
}
