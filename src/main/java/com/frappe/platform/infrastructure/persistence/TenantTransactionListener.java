package com.frappe.platform.infrastructure.persistence;

import com.frappe.platform.TenantScope;
import java.util.function.Supplier;
import org.jspecify.annotations.Nullable;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.TransactionExecution;
import org.springframework.transaction.TransactionExecutionListener;
import org.springframework.transaction.TransactionStatus;
import org.springframework.transaction.support.TransactionSynchronizationManager;

/**
 * Starts every new transaction of a thread with a bound tenant by setting {@code app.tenant_id} for that transaction
 * only ({@code set_config(..., true)}), so a pooled connection carries nothing to its next use. Without a bound tenant
 * nothing is set, and the row-level security policies show no rows.
 */
final class TenantTransactionListener implements TransactionExecutionListener {

    private final TenantScope tenants;
    private final JdbcTemplate jdbc;
    private final Supplier<PlatformTransactionManager> transactionManager;

    /**
     * Creates the listener.
     *
     * @param tenants the tenant bound to the thread beginning a transaction
     * @param jdbc runs on the transaction's connection
     * @param transactionManager the manager this listener is added to, which rolls back a transaction whose setting
     *     failed
     */
    TenantTransactionListener(
            TenantScope tenants, JdbcTemplate jdbc, Supplier<PlatformTransactionManager> transactionManager) {
        this.tenants = tenants;
        this.jdbc = jdbc;
        this.transactionManager = transactionManager;
    }

    @Override
    public void afterBegin(TransactionExecution transaction, @Nullable Throwable beginFailure) {
        if (beginFailure != null || !TransactionSynchronizationManager.isActualTransactionActive()) {
            return;
        }
        var tenantId = tenants.current();
        if (tenantId.isEmpty()) {
            return;
        }
        try {
            jdbc.queryForObject(
                    "select set_config('app.tenant_id', ?, true)",
                    String.class,
                    tenantId.get().toString());
        } catch (RuntimeException setFailure) {
            // AbstractPlatformTransactionManager.startTransaction (spring-tx 7.0.9, line 539) calls afterBegin after
            // doBegin and prepareSynchronization without a try: an exception here would leave the transaction begun,
            // its connection and synchronization bound to the thread, and the caller never gets the status to end it.
            rollBack(transaction, setFailure);
            throw setFailure;
        }
    }

    private void rollBack(TransactionExecution transaction, RuntimeException setFailure) {
        try {
            transactionManager.get().rollback((TransactionStatus) transaction);
        } catch (RuntimeException rollbackFailure) {
            setFailure.addSuppressed(rollbackFailure);
        }
    }
}
