package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import com.github.kagkarlsson.scheduler.SchedulerClient;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.context.ActiveProfiles;

/**
 * The application's scheduler works on the Flyway-owned {@code platform.scheduled_tasks} as the runtime role. The probe
 * task is unknown to every scheduler, so none of them ever runs it; the row is removed after the test.
 */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class SchedulerTableIntegrationTests {

    static final String PROBE_TASK = "platform.table-probe";

    final String instance = UUID.randomUUID().toString();

    @Autowired
    SchedulerClient scheduler;

    @Autowired
    JdbcClient jdbc;

    @AfterEach
    void deleteProbe() {
        jdbc.sql("delete from platform.scheduled_tasks where task_name = ? and task_instance = ?")
                .params(PROBE_TASK, instance)
                .update();
    }

    @Test
    void schedulesIntoTheFlywayOwnedPlatformTable() {
        // Given
        var executionTime = Instant.parse("2100-01-01T00:00:00Z");

        // When
        var scheduled = scheduler.scheduleIfNotExists(new TaskInstance<>(PROBE_TASK, instance), executionTime);

        // Then
        assertThat(scheduled).isTrue();
        var stored = jdbc.sql("select execution_time from platform.scheduled_tasks"
                        + " where task_name = ? and task_instance = ?")
                .params(PROBE_TASK, instance)
                .query(Instant.class)
                .single();
        assertThat(stored).isEqualTo(executionTime);
    }

    @Test
    void schedulingTheSameNaturalKeyTwiceKeepsOneExecution() {
        // Given
        var probe = new TaskInstance<>(PROBE_TASK, instance);
        scheduler.scheduleIfNotExists(probe, Instant.parse("2100-01-01T00:00:00Z"));

        // When
        var scheduledAgain = scheduler.scheduleIfNotExists(probe, Instant.parse("2100-06-01T00:00:00Z"));

        // Then
        assertThat(scheduledAgain).isFalse();
        var rows = jdbc.sql("select count(*) from platform.scheduled_tasks where task_name = ? and task_instance = ?")
                .params(PROBE_TASK, instance)
                .query(Long.class)
                .single();
        assertThat(rows).isOne();
    }
}
