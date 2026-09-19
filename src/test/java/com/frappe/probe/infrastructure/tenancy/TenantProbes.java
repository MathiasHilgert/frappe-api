package com.frappe.probe.infrastructure.tenancy;

import com.frappe.platform.OneTimeTask;
import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.TaskName;
import com.frappe.platform.TenantScope;
import com.frappe.probe.TenantProbesRequested;
import com.frappe.probe.application.ListTenantProbes;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.modulith.events.ApplicationModuleListener;
import org.springframework.transaction.annotation.Propagation;

/**
 * The probe module's tenant-bound adapters, written the way a real module writes them: an event listener and a
 * scheduled task that bind the tenant of the event or the task data through {@link TenantScope} before calling the use
 * case. What each one saw is kept in {@link Sightings}. Importing this registers them.
 */
@TestConfiguration(proxyBeanMethods = false)
public class TenantProbes {

    /** The probe task's name. */
    public static final TaskName READ_TASK = TaskName.of("probe.read-tenant-probes");

    /**
     * Data of one probe task run.
     *
     * @param runId identifies the run in {@link Sightings}
     * @param tenantId the tenant to read as
     */
    public record ReadRun(UUID runId, UUID tenantId) {}

    /** The probe ids each listener call or task run saw, by event id or run id. */
    public static final class Sightings {

        private final Map<UUID, List<UUID>> seen = new ConcurrentHashMap<>();

        void record(UUID id, List<UUID> probeIds) {
            seen.put(id, probeIds);
        }

        /**
         * What a listener call or task run saw.
         *
         * @param id the event id or run id
         * @return the probe ids it saw, or empty before it ran
         */
        public Optional<List<UUID>> of(UUID id) {
            return Optional.ofNullable(seen.get(id));
        }
    }

    /** Reads the requested tenant's probes after the publishing transaction committed. */
    static class Listener {

        private final TenantScope tenants;
        private final ListTenantProbes list;
        private final Sightings sightings;

        Listener(TenantScope tenants, ListTenantProbes list, Sightings sightings) {
            this.tenants = tenants;
            this.list = list;
            this.sightings = sightings;
        }

        // No transaction of its own: the tenant is bound first, then the use case starts one.
        @ApplicationModuleListener(propagation = Propagation.NOT_SUPPORTED)
        void on(TenantProbesRequested event) {
            sightings.record(event.eventId(), tenants.callAs(event.tenantId(), list::list));
        }
    }

    @Bean
    Sightings tenantProbeSightings() {
        return new Sightings();
    }

    @Bean
    Listener tenantProbeListener(TenantScope tenants, ListTenantProbes list, Sightings sightings) {
        return new Listener(tenants, list, sightings);
    }

    @Bean
    OneTimeTask<ReadRun> readTenantProbes(
            ScheduledTasks tasks, TenantScope tenants, ListTenantProbes list, Sightings sightings) {
        return tasks.oneTime(
                READ_TASK,
                ReadRun.class,
                run -> sightings.record(run.runId(), tenants.callAs(run.tenantId(), list::list)));
    }
}
