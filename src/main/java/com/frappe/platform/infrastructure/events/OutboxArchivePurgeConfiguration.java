package com.frappe.platform.infrastructure.events;

import com.frappe.platform.RecurringTask;
import com.frappe.platform.ScheduledTasks;
import io.micrometer.observation.ObservationRegistry;
import java.time.Clock;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Schedules the outbox archive purge (FAPI-9) as a cluster-safe scheduled task. {@link OutboxArchivePurgeRepository}
 * is a {@code @Repository}, found by component scanning like {@link OutboxRecoveryRepository}.
 */
@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(OutboxArchivePurgeProperties.class)
class OutboxArchivePurgeConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    OutboxArchivePurgeConfiguration() {}

    /**
     * Runs one purge.
     *
     * @param repository purge queries on the outbox archive
     * @param properties purge settings
     * @param clock the application clock
     * @param observations records every run
     * @return the purger
     */
    @Bean
    OutboxArchivePurger outboxArchivePurger(
            OutboxArchivePurgeRepository repository,
            OutboxArchivePurgeProperties properties,
            Clock clock,
            ObservationRegistry observations) {
        return new OutboxArchivePurger(repository, properties, clock, observations);
    }

    /**
     * The purge task, a {@link com.frappe.platform.ScheduledTask} bean: {@code SchedulingConfiguration} builds the
     * scheduler from every such bean.
     *
     * @param tasks the platform's task conventions
     * @param purger runs one purge
     * @param properties purge settings
     * @return the task
     */
    @Bean
    RecurringTask<Void> outboxArchivePurgeTask(
            ScheduledTasks tasks, OutboxArchivePurger purger, OutboxArchivePurgeProperties properties) {
        return OutboxArchivePurgeTask.declare(tasks, purger, properties.purgeInterval());
    }
}
