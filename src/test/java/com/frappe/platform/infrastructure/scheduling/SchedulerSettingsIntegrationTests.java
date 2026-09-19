package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestcontainersConfiguration;
import com.github.kagkarlsson.scheduler.boot.config.DbSchedulerProperties;
import com.github.kagkarlsson.scheduler.stats.MicrometerStatsRegistry;
import com.github.kagkarlsson.scheduler.stats.StatsRegistry;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;

/** The {@code db-scheduler.*} settings the application runs with (application.properties). */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class SchedulerSettingsIntegrationTests {

    @Autowired
    DbSchedulerProperties settings;

    @Autowired
    StatsRegistry stats;

    @Autowired
    MeterRegistry meters;

    @Test
    void anExecutionOfADeadInstanceIsTakenOverAfterAboutNinetySeconds() {
        assertThat(settings.getHeartbeatInterval()).isEqualTo(Duration.ofSeconds(15));
        assertThat(settings.getMissedHeartbeatsLimit()).isEqualTo(6);
        assertThat(settings.getHeartbeatInterval().multipliedBy(settings.getMissedHeartbeatsLimit()))
                .isEqualTo(Duration.ofSeconds(90));
    }

    @Test
    void runsOnTheFlywayOwnedTableOnceTheContextIsReadyAndDrainsQuicklyOnShutdown() {
        assertThat(settings.getTableName()).isEqualTo("platform.scheduled_tasks");
        assertThat(settings.isDelayStartupUntilContextReady()).isTrue();
        assertThat(settings.getShutdownMaxWait()).isEqualTo(Duration.ofSeconds(10));
    }

    @Test
    void theLibraryMetersOfEveryTaskAreRegisteredInTheApplicationsMeterRegistry() {
        assertThat(stats).isInstanceOf(MicrometerStatsRegistry.class);
        // The starter's registry creates a task's meters with its first completed run (the recovery runs at startup).
        await().atMost(Duration.ofSeconds(30))
                .untilAsserted(() -> assertThat(meters.find("dbscheduler_task_completions")
                                .tag("task", "platform.outbox-recovery")
                                .counters())
                        .isNotEmpty());
    }
}
