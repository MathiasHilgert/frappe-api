package com.frappe.probe.application;

import com.frappe.platform.CommandUseCase;
import java.util.UUID;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Transactional;

/** Saves a tenant probe row for the tenant given, which the row-level security policy may refuse. */
@CommandUseCase
public class RecordTenantProbe {

    private final JdbcTemplate jdbc;

    RecordTenantProbe(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @Transactional
    public UUID record(UUID tenantId, UUID probeId) {
        jdbc.update(
                "insert into fixture.tenant_probe (id, tenant_id, label) values (?, ?, ?)", probeId, tenantId, "new");
        return probeId;
    }
}
