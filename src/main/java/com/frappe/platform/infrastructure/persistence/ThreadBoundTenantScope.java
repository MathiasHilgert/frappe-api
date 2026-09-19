package com.frappe.platform.infrastructure.persistence;

import com.frappe.platform.TenantScope;
import java.util.Objects;
import java.util.Optional;
import java.util.UUID;
import java.util.function.Supplier;
import org.springframework.transaction.support.TransactionSynchronizationManager;

/**
 * Holds the bound tenant per thread, in this bean rather than in static state. {@link TenantTransactionListener} reads
 * it when a transaction begins.
 */
final class ThreadBoundTenantScope implements TenantScope {

    private final ThreadLocal<UUID> bound = new ThreadLocal<>();

    /** Creates a scope with no tenant bound on any thread. */
    ThreadBoundTenantScope() {}

    @Override
    public <T> T callAs(UUID tenantId, Supplier<T> work) {
        Objects.requireNonNull(tenantId, "tenantId");
        Objects.requireNonNull(work, "work");
        var current = bound.get();
        if (tenantId.equals(current)) {
            return work.get();
        }
        if (current != null) {
            throw new IllegalStateException("Another tenant is already bound to this thread");
        }
        if (TransactionSynchronizationManager.isActualTransactionActive()) {
            throw new IllegalStateException(
                    "A tenant must be bound before the transaction starts; the running one would not see it");
        }
        bound.set(tenantId);
        try {
            return work.get();
        } finally {
            bound.remove();
        }
    }

    @Override
    public void runAs(UUID tenantId, Runnable work) {
        Objects.requireNonNull(work, "work");
        callAs(tenantId, () -> {
            work.run();
            return null;
        });
    }

    @Override
    public Optional<UUID> current() {
        return Optional.ofNullable(bound.get());
    }
}
