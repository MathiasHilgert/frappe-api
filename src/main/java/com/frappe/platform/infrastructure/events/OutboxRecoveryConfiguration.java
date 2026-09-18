package com.frappe.platform.infrastructure.events;

import java.time.Clock;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Configuration;
import org.springframework.modulith.events.FailedEventPublications;
import org.springframework.scheduling.annotation.EnableScheduling;
import org.springframework.scheduling.annotation.SchedulingConfigurer;
import org.springframework.scheduling.config.ScheduledTaskRegistrar;

/**
 * Schedules outbox recovery. Spring Modulith's staleness monitor ({@code spring.modulith.events.staleness.*}) stays
 * off: it judges every status by the publication date, so this job detects stuck attempts itself.
 */
@Configuration(proxyBeanMethods = false)
@EnableScheduling
@EnableConfigurationProperties(OutboxRecoveryProperties.class)
class OutboxRecoveryConfiguration implements SchedulingConfigurer {

    private final FailedPublicationResubmitter resubmitter;
    private final OutboxRecoveryProperties properties;

    /**
     * Creates the configuration; instantiated by Spring.
     *
     * @param failedPublications Modulith's entry point for resubmitting failed publications
     * @param outbox recovery queries on the outbox tables
     * @param properties recovery settings
     * @param clock the application clock
     */
    OutboxRecoveryConfiguration(
            FailedEventPublications failedPublications,
            OutboxRecoveryRepository outbox,
            OutboxRecoveryProperties properties,
            Clock clock) {
        this.resubmitter = new FailedPublicationResubmitter(failedPublications, outbox, properties, clock);
        this.properties = properties;
    }

    // Fixed delay, not rate: a run that waits on a slow NATS never overlaps the next one.
    @Override
    public void configureTasks(ScheduledTaskRegistrar registrar) {
        registrar.addFixedDelayTask(resubmitter, properties.interval());
    }
}
