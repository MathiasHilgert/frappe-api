package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.EntitySchedule;
import com.frappe.platform.EntityTask;
import com.frappe.platform.OneTimeTask;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * Scheduling through the application's task beans joins the caller's transaction: it commits or rolls back with it,
 * like an outbox row. The probe runs are dated far ahead, so the scheduler never picks them.
 */
@SpringBootTest(
        properties = {
            "db-scheduler.polling-interval=100ms",
            "frappe.scheduling.initial-backoff=200ms",
            "frappe.scheduling.max-retries=1"
        })
@Import({TestcontainersConfiguration.class, SchedulingProbes.class})
@ActiveProfiles("local")
class SchedulingTransactionIntegrationTests {

    static final Instant FAR_AHEAD = Instant.parse("2100-01-01T00:00:00Z");

    static final EntitySchedule YEARLY = new EntitySchedule("0 0 4 1 1 *", ZoneOffset.UTC);

    final String key = "probe-" + UUID.randomUUID();

    @Autowired
    OneTimeTask<String> quietProbe;

    @Autowired
    EntityTask entityProbe;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    JdbcClient jdbc;

    @AfterEach
    void deleteProbes() {
        jdbc.sql("delete from platform.scheduled_tasks where task_instance = ?")
                .param(key)
                .update();
    }

    @Test
    void schedulingInARolledBackTransactionLeavesNoExecution() {
        // When
        transactions.executeWithoutResult(status -> {
            quietProbe.schedule(key, key, FAR_AHEAD);
            entityProbe.schedule(key, YEARLY);
            status.setRollbackOnly();
        });

        // Then
        assertThat(executionsOf(SchedulingProbes.QUIET.value())).isZero();
        assertThat(executionsOf(SchedulingProbes.ENTITY.value())).isZero();
    }

    @Test
    void schedulingInACommittedTransactionKeepsTheExecutions() {
        // When
        transactions.executeWithoutResult(status -> {
            quietProbe.schedule(key, key, FAR_AHEAD);
            entityProbe.schedule(key, YEARLY);
        });

        // Then
        assertThat(executionsOf(SchedulingProbes.QUIET.value())).isOne();
        assertThat(executionsOf(SchedulingProbes.ENTITY.value())).isOne();
    }

    private long executionsOf(String taskName) {
        return jdbc.sql("select count(*) from platform.scheduled_tasks where task_name = ? and task_instance = ?")
                .params(taskName, key)
                .query(Long.class)
                .single();
    }
}
