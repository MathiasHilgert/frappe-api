package com.frappe.probe.application;

import com.frappe.platform.CommandUseCase;
import java.util.UUID;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Transactional;

/** Relabels a tenant probe row by id, whichever tenant it belongs to. */
@CommandUseCase
public class RelabelTenantProbe {

    private final JdbcTemplate jdbc;

    RelabelTenantProbe(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @Transactional
    public int relabel(UUID probeId, String label) {
        return jdbc.update("update fixture.tenant_probe set label = ? where id = ?", label, probeId);
    }
}
