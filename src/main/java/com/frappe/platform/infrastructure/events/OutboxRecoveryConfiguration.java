package com.frappe.platform.infrastructure.events;

import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Configuration;
import org.springframework.modulith.events.FailedEventPublications;
import org.springframework.scheduling.annotation.EnableScheduling;
import org.springframework.scheduling.annotation.SchedulingConfigurer;
import org.springframework.scheduling.config.ScheduledTaskRegistrar;

/**
 * Schedules outbox recovery. {@link EnableScheduling} also activates Spring Modulith's staleness monitor
 * ({@code spring.modulith.events.staleness.*}), which marks stuck publications failed so this job resubmits them.
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
     * @param properties recovery interval and batch size
     */
    OutboxRecoveryConfiguration(FailedEventPublications failedPublications, OutboxRecoveryProperties properties) {
        this.resubmitter = new FailedPublicationResubmitter(failedPublications, properties);
        this.properties = properties;
    }

    // Fixed delay, not rate: a run that waits on a slow NATS never overlaps the next one.
    @Override
    public void configureTasks(ScheduledTaskRegistrar registrar) {
        registrar.addFixedDelayTask(resubmitter, properties.interval());
    }
}
