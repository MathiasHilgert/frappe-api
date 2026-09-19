package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import com.github.kagkarlsson.scheduler.boot.config.DbSchedulerProperties;
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
}
