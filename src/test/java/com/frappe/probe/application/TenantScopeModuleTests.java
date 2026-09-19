package com.frappe.probe.application;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.OneTimeTask;
import com.frappe.platform.TenantScope;
import com.frappe.probe.TenantProbesRequested;
import com.frappe.probe.infrastructure.tenancy.TenantProbes;
import com.frappe.probe.infrastructure.tenancy.TenantProbes.ReadRun;
import com.frappe.probe.infrastructure.tenancy.TenantProbes.Sightings;
import java.time.Clock;
import java.time.Duration;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.context.annotation.Import;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.test.ApplicationModuleTest;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.TestPropertySource;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * Row-level security on a tenant-scoped fixture table, run as {@code frappe_app} on real Postgres: a use case, an
 * event listener and a scheduled task each see only the tenant bound through {@link TenantScope}. Every test creates
 * its own tenants, so rows of earlier runs in the reused database are invisible to it.
 */
@ApplicationModuleTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TenantProbes.class})
@TestPropertySource(properties = "db-scheduler.polling-interval=100ms")
@ActiveProfiles("local")
class TenantScopeModuleTests {

    private final TenantScope tenants;
    private final RecordTenantProbe record;
    private final RelabelTenantProbe relabel;
    private final ListTenantProbes list;
    private final IdGenerator ids;
    private final Clock clock;
    private final JdbcTemplate jdbc;
    private final TransactionTemplate transactions;
    private final DomainEventPublisher events;
    private final OneTimeTask<ReadRun> readTask;
    private final Sightings sightings;

    private UUID tenantA;
    private UUID tenantB;
    private UUID probeOfA;
    private UUID probeOfB;

    @Autowired
    TenantScopeModuleTests(
            TenantScope tenants,
            RecordTenantProbe record,
            RelabelTenantProbe relabel,
            ListTenantProbes list,
            IdGenerator ids,
            Clock clock,
            JdbcTemplate jdbc,
            TransactionTemplate transactions,
            DomainEventPublisher events,
            OneTimeTask<ReadRun> readTask,
            Sightings sightings) {
        this.tenants = tenants;
        this.record = record;
        this.relabel = relabel;
        this.list = list;
        this.ids = ids;
        this.clock = clock;
        this.jdbc = jdbc;
        this.transactions = transactions;
        this.events = events;
        this.readTask = readTask;
        this.sightings = sightings;
    }

    @BeforeEach
    void rowsOfTwoTenants() {
        tenantA = ids.newId();
        tenantB = ids.newId();
        probeOfA = tenants.callAs(tenantA, () -> record.record(tenantA, ids.newId()));
        probeOfB = tenants.callAs(tenantB, () -> record.record(tenantB, ids.newId()));
    }

    @Test
    void aUseCaseInATenantScopeSeesAndUpdatesOnlyThatTenantsRows() {
        // When
        var visible = tenants.callAs(tenantA, list::list);
        var updatedOfB = tenants.callAs(tenantA, () -> relabel.relabel(probeOfB, "taken"));
        var updatedOfA = tenants.callAs(tenantA, () -> relabel.relabel(probeOfA, "kept"));

        // Then
        assertThat(visible).containsExactly(probeOfA);
        assertThat(updatedOfB).isZero();
        assertThat(updatedOfA).isOne();
    }

    @Test
    void withoutATenantNoRowIsVisible() {
        assertThat(list.list()).isEmpty();
    }

    @Test
    void theTenantPolicyRefusesARowOfAnotherTenant() {
        assertThatThrownBy(() -> tenants.runAs(tenantA, () -> record.record(tenantB, ids.newId())))
                .isInstanceOf(DataAccessException.class)
                .rootCause()
                .hasMessageContaining("violates row-level security policy");
    }

    @Test
    void aPooledConnectionKeepsNoTenantAfterItsTransaction() {
        // Given: the pool hands a thread back the connection it used last
        var boundConnection = tenants.callAs(tenantA, () -> transactions.execute(status -> backendPid()));

        // When
        var unboundConnection = transactions.execute(status -> backendPid());
        var visible = list.list();

        // Then
        assertThat(unboundConnection).isEqualTo(boundConnection);
        assertThat(visible).isEmpty();
    }

    @Test
    void aListenerBindingTheEventsTenantSeesOnlyThatTenant() {
        // Given
        var eventId = ids.newId();

        // When
        transactions.executeWithoutResult(
                status -> events.publish(new TenantProbesRequested(eventId, clock.instant(), tenantA, 1, 1, tenantA)));

        // Then
        assertThat(awaitSighting(eventId)).containsExactly(probeOfA);
    }

    @Test
    void aScheduledTaskBindingItsTenantSeesOnlyThatTenant() {
        // Given
        var runId = ids.newId();

        // When
        readTask.schedule(runId.toString(), new ReadRun(runId, tenantB), clock.instant());

        // Then
        assertThat(awaitSighting(runId)).containsExactly(probeOfB);
    }

    @Test
    void bindingAnotherTenantInsideABoundScopeIsRejected() {
        assertThatThrownBy(() -> tenants.runAs(tenantA, () -> tenants.runAs(tenantB, list::list)))
                .isInstanceOf(IllegalStateException.class);
    }

    @Test
    void bindingInsideARunningTransactionIsRejected() {
        assertThatThrownBy(() -> transactions.executeWithoutResult(status -> tenants.runAs(tenantA, list::list)))
                .isInstanceOf(IllegalStateException.class);
    }

    private Integer backendPid() {
        return jdbc.queryForObject("select pg_backend_pid()", Integer.class);
    }

    private List<UUID> awaitSighting(UUID id) {
        return await().atMost(Duration.ofSeconds(10))
                .until(() -> sightings.of(id), seen -> seen.isPresent())
                .orElseThrow();
    }
}
