package com.frappe.platform.infrastructure.events;

import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import java.time.Clock;
import java.util.Collection;
import java.util.function.Supplier;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.ApplicationContext;
import org.springframework.context.ApplicationListener;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.event.EventListener;
import org.springframework.context.support.AbstractApplicationContext;
import org.springframework.modulith.events.core.EventPublicationRepository;
import org.springframework.modulith.events.core.EventSerializer;
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
     * @param repository the registry's repository, for the guarded state transitions
     * @param serializer the registry's event serializer
     * @param context the application context, for the listener a publication targets
     * @param outbox recovery queries on the outbox tables
     * @param metrics the dead-letter gauge
     * @param properties recovery settings
     * @param clock the application clock
     */
    OutboxRecoveryConfiguration(
            EventPublicationRepository repository,
            EventSerializer serializer,
            ApplicationContext context,
            OutboxRecoveryRepository outbox,
            DeadLetterMetrics metrics,
            OutboxRecoveryProperties properties,
            Clock clock) {
        var redelivery =
                new PublicationRedelivery(repository, serializer, listenersOf(context), context.getClassLoader());
        this.resubmitter = new FailedPublicationResubmitter(redelivery, outbox, metrics, properties, clock);
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

    /**
     * Runs a recovery pass at once when a messaging transport came back; published by the transport adapter, which
     * does not know the outbox.
     *
     * @param recovered the transport that came back
     */
    @EventListener
    void onTransportRecovered(MessagingTransportRecovered recovered) {
        resubmitter.onTransportRecovered(recovered);
    }

    // Fixed delay, not rate: a run that waits on a slow NATS never overlaps the next one.
    @Override
    public void configureTasks(ScheduledTaskRegistrar registrar) {
        registrar.addFixedDelayTask(resubmitter, properties.interval());
    }
}
