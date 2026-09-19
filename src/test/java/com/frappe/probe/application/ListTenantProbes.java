package com.frappe.probe.application;

import com.frappe.platform.QueryUseCase;
import java.util.List;
import java.util.UUID;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Transactional;

/** Lists the ids of every tenant probe row, without filtering by tenant: row-level security does that. */
@QueryUseCase
public class ListTenantProbes {

    private final JdbcTemplate jdbc;

    ListTenantProbes(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @Transactional(readOnly = true)
    public List<UUID> list() {
        return jdbc.queryForList("select id from fixture.tenant_probe", UUID.class);
    }
}
