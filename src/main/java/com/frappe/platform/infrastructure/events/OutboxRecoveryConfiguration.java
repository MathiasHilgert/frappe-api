package com.frappe.platform.infrastructure.events;

import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.task.helper.RecurringTask;
import io.micrometer.observation.ObservationRegistry;
import java.time.Clock;
import java.util.Collection;
import java.util.function.Supplier;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.ApplicationContext;
import org.springframework.context.ApplicationListener;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.support.AbstractApplicationContext;
import org.springframework.modulith.events.core.EventPublicationRepository;
import org.springframework.modulith.events.core.EventSerializer;

/**
 * Schedules outbox recovery as a cluster-safe db-scheduler task. Spring Modulith's staleness monitor ({@code
 * spring.modulith.events.staleness.*}) stays off: it judges every status by the publication date, so the recovery pass
 * detects stuck attempts itself.
 */
@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(OutboxRecoveryProperties.class)
class OutboxRecoveryConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    OutboxRecoveryConfiguration() {}

    /**
     * The dead-letter gauge; Spring Boot binds every {@code MeterBinder} bean to the meter registry.
     *
     * @return the metrics
     */
    @Bean
    static DeadLetterMetrics deadLetterMetrics() {
        return new DeadLetterMetrics();
    }

    /**
     * Runs one recovery pass.
     *
     * @param repository the registry's repository, for the guarded state transitions
     * @param serializer the registry's event serializer
     * @param context the application context, for the listener a publication targets
     * @param outbox recovery queries on the outbox tables
     * @param metrics the dead-letter gauge
     * @param properties recovery settings
     * @param clock the application clock
     * @param observations records recovery passes and redeliveries
     * @return the resubmitter
     */
    @Bean
    FailedPublicationResubmitter failedPublicationResubmitter(
            EventPublicationRepository repository,
            EventSerializer serializer,
            ApplicationContext context,
            OutboxRecoveryRepository outbox,
            DeadLetterMetrics metrics,
            OutboxRecoveryProperties properties,
            Clock clock,
            ObservationRegistry observations) {
        var redelivery =
                new PublicationRedelivery(repository, serializer, listenersOf(context), context.getClassLoader());
        return new FailedPublicationResubmitter(redelivery, outbox, metrics, properties, clock, observations);
    }

    /**
     * The recovery task, collected by the db-scheduler starter.
     *
     * @param tasks the platform's task conventions
     * @param resubmitter runs one pass
     * @param properties recovery settings
     * @return the task
     */
    @Bean
    RecurringTask<Trigger> outboxRecoveryTask(
            ScheduledTasks tasks, FailedPublicationResubmitter resubmitter, OutboxRecoveryProperties properties) {
        return OutboxRecoveryTask.declare(tasks, resubmitter, properties.interval());
    }

    /**
     * Runs a pass at once when a messaging transport came back.
     *
     * @param scheduler the application's scheduler
     * @param clock the application clock
     * @return the trigger
     */
    @Bean
    OutboxRecoveryTrigger outboxRecoveryTrigger(Scheduler scheduler, Clock clock) {
        return new OutboxRecoveryTrigger(scheduler, clock);
    }

    // Modulith resolves the target listener from the multicaster's listeners; the context keeps the same set
    // (listener methods are added through addApplicationListener) and, unlike the multicaster, exposes it publicly.
    private static Supplier<Collection<ApplicationListener<?>>> listenersOf(ApplicationContext context) {
        if (!(context instanceof AbstractApplicationContext listenerRegistry)) {
            throw new IllegalStateException(
                    "Outbox recovery needs an AbstractApplicationContext to find event listeners, got "
                            + context.getClass().getName());
        }
        return listenerRegistry::getApplicationListeners;
    }
}
