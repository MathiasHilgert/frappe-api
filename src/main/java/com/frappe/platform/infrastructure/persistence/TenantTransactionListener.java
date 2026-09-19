package com.frappe.platform.infrastructure.persistence;

import com.frappe.platform.TenantScope;
import org.jspecify.annotations.Nullable;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.TransactionExecution;
import org.springframework.transaction.TransactionExecutionListener;
import org.springframework.transaction.support.TransactionSynchronizationManager;

/**
 * Starts every new transaction of a thread with a bound tenant by setting {@code app.tenant_id} for that transaction
 * only ({@code set_config(..., true)}), so a pooled connection carries nothing to its next use. Without a bound tenant
 * nothing is set, and the row-level security policies show no rows.
 */
final class TenantTransactionListener implements TransactionExecutionListener {

    private final TenantScope tenants;
    private final JdbcTemplate jdbc;

    /**
     * Creates the listener.
     *
     * @param tenants the tenant bound to the thread beginning a transaction
     * @param jdbc runs on the transaction's connection
     */
    TenantTransactionListener(TenantScope tenants, JdbcTemplate jdbc) {
        this.tenants = tenants;
        this.jdbc = jdbc;
    }

    @Override
    public void afterBegin(TransactionExecution transaction, @Nullable Throwable beginFailure) {
        if (beginFailure != null || !TransactionSynchronizationManager.isActualTransactionActive()) {
            return;
        }
        tenants.current()
                .ifPresent(tenantId -> jdbc.queryForObject(
                        "select set_config('app.tenant_id', ?, true)", String.class, tenantId.toString()));
    }
}
