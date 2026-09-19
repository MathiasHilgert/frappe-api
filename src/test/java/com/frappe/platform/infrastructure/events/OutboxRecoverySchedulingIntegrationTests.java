package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestcontainersConfiguration;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.ApplicationContext;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.scheduling.annotation.ScheduledAnnotationBeanPostProcessor;
import org.springframework.test.context.ActiveProfiles;

/** Outbox recovery is one cluster-wide db-scheduler execution; Spring's in-process scheduling is gone. */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class OutboxRecoverySchedulingIntegrationTests {

    @Autowired
    JdbcClient jdbc;

    @Autowired
    ApplicationContext context;

    @Test
    void recoveryIsOneExecutionInTheSchedulerTable() {
        await().atMost(Duration.ofSeconds(10))
                .untilAsserted(() -> assertThat(jdbc.sql("select task_instance from platform.scheduled_tasks"
                                        + " where task_name = 'platform.outbox-recovery'")
                                .query(String.class)
                                .list())
                        .containsExactly("recurring"));
    }

    @Test
    void springsInProcessSchedulingIsNotEnabled() {
        assertThat(context.getBeanNamesForType(ScheduledAnnotationBeanPostProcessor.class))
                .isEmpty();
    }
}
