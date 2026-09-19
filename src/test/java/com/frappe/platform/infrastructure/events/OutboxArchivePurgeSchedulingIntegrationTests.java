package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestcontainersConfiguration;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.context.ActiveProfiles;

/** The purge is one cluster-wide db-scheduler execution, never Spring's in-process scheduling. */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class OutboxArchivePurgeSchedulingIntegrationTests {

    @Autowired
    JdbcClient jdbc;

    @Test
    void purgeIsOneExecutionInTheSchedulerTable() {
        await().atMost(Duration.ofSeconds(10))
                .untilAsserted(() -> assertThat(jdbc.sql("select task_instance from platform.scheduled_tasks"
                                        + " where task_name = 'platform.purge-event-archive'")
                                .query(String.class)
                                .list())
                        .containsExactly("recurring"));
    }
}
