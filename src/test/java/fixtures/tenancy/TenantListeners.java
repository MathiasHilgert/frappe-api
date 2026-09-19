package fixtures.tenancy;

import com.frappe.platform.TenantScope;
import java.util.UUID;
import org.springframework.modulith.events.ApplicationModuleListener;
import org.springframework.transaction.annotation.Propagation;

/** Event listeners that bind a tenant, for the tenant listener rule. */
public final class TenantListeners {

    private TenantListeners() {}

    /** Opens no transaction before binding: compliant. */
    public static class Compliant {

        private final TenantScope tenants;

        Compliant(TenantScope tenants) {
            this.tenants = tenants;
        }

        @ApplicationModuleListener(propagation = Propagation.NOT_SUPPORTED)
        void on(UUID tenantId) {
            tenants.runAs(tenantId, () -> {});
        }
    }

    /** Keeps the default REQUIRES_NEW transaction, so binding throws on every delivery. */
    public static class Transactional {

        private final TenantScope tenants;

        Transactional(TenantScope tenants) {
            this.tenants = tenants;
        }

        @ApplicationModuleListener
        void on(UUID tenantId) {
            tenants.runAs(tenantId, () -> {});
        }
    }

    /** Binds no tenant, so its transaction is fine. */
    public static class Untenanted {

        @ApplicationModuleListener
        void on(UUID tenantId) {}
    }
}
