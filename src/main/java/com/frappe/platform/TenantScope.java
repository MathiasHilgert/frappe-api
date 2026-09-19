package com.frappe.platform;

import java.util.Optional;
import java.util.UUID;
import java.util.function.Supplier;

/**
 * Binds the current thread to one tenant, so that every transaction it starts sees only that tenant's rows. While a
 * tenant is bound, each new transaction begins with {@code set_config('app.tenant_id', <tenant>, true)}: the setting
 * is transaction-local, so a pooled connection carries nothing to its next use. The row-level security policies of
 * tenant-scoped tables read it; with no tenant bound they show no rows.
 *
 * <p>Bind before the transaction starts, around the use case call: in an event listener from the event's tenant, in a
 * scheduled task from the entity's tenant, in a request from the verified tenant of the path. Never bind a tenant taken
 * from unverified client input.
 *
 * <pre>{@code
 * tenants.runAs(event.tenantId(), () -> closeTab.close(event.tabId()));
 * }</pre>
 *
 * <p>Binding a different tenant inside a bound scope, or binding while a transaction is running (it would not see
 * the tenant), throws {@link IllegalStateException}. Binding the tenant already bound runs the work unchanged.
 */
public interface TenantScope {

    /**
     * Runs work with a tenant bound to the current thread, unbinding it afterwards.
     *
     * @param <T> the result type
     * @param tenantId the tenant
     * @param work the work, typically one use case call
     * @return the work's result
     * @throws IllegalStateException if another tenant is bound, or a transaction is running
     */
    <T> T callAs(UUID tenantId, Supplier<T> work);

    /**
     * Runs work with a tenant bound to the current thread, unbinding it afterwards.
     *
     * @param tenantId the tenant
     * @param work the work, typically one use case call
     * @throws IllegalStateException if another tenant is bound, or a transaction is running
     */
    void runAs(UUID tenantId, Runnable work);

    /**
     * The tenant bound to the current thread.
     *
     * @return the tenant, or empty when none is bound
     */
    Optional<UUID> current();
}
