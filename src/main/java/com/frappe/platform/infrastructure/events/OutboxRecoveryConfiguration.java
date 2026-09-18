package com.frappe.platform.infrastructure.events;

import java.time.Clock;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.modulith.events.IncompleteEventPublications;
import org.springframework.scheduling.annotation.EnableScheduling;
import org.springframework.scheduling.annotation.SchedulingConfigurer;
import org.springframework.scheduling.config.ScheduledTaskRegistrar;

/**
 * Schedules outbox recovery. Spring Modulith's staleness monitor ({@code spring.modulith.events.staleness.*}) stays
 * off: it judges every status by the publication date, so the recovery run detects stuck attempts itself.
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
     * @param incompletePublications Modulith's entry point for resubmitting publications
     * @param outbox recovery queries on the outbox tables
     * @param metrics the dead-letter gauge
     * @param properties recovery settings
     * @param clock the application clock
     */
    OutboxRecoveryConfiguration(
            IncompleteEventPublications incompletePublications,
            OutboxRecoveryRepository outbox,
            DeadLetterMetrics metrics,
            OutboxRecoveryProperties properties,
            Clock clock) {
        this.resubmitter = new FailedPublicationResubmitter(incompletePublications, outbox, metrics, properties, clock);
        this.properties = properties;
    }

    /**
     * The dead-letter gauge; Spring Boot binds every {@code MeterBinder} bean to the meter registry.
     *
     * @return the metrics
     */
    @Bean
    static DeadLetterMetrics deadLetterMetrics() {
        return new DeadLetterMetrics();
    }

    // Fixed delay, not rate: a run that waits on a slow NATS never overlaps the next one.
    @Override
    public void configureTasks(ScheduledTaskRegistrar registrar) {
        registrar.addFixedDelayTask(resubmitter, properties.interval());
    }
}
