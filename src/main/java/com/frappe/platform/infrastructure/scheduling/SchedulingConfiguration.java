package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.ScheduledTasks;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.boot.autoconfigure.Jackson3Serializer;
import com.github.kagkarlsson.scheduler.boot.config.DbSchedulerCustomizer;
import com.github.kagkarlsson.scheduler.serializer.Serializer;
import io.micrometer.observation.ObservationRegistry;
import java.util.Optional;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Wires the platform's scheduling conventions into the db-scheduler starter, which collects every {@code Task}, {@code
 * ExecutionInterceptor} and {@code SchedulerListener} bean and builds the one {@link Scheduler} from {@code
 * db-scheduler.*}.
 */
@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(SchedulingProperties.class)
class SchedulingConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    SchedulingConfiguration() {}

    /**
     * The task factory modules declare their tasks with.
     *
     * @param properties the retry settings
     * @return the conventions
     */
    @Bean
    ScheduledTasks scheduledTasks(SchedulingProperties properties) {
        return new ConventionalScheduledTasks(properties);
    }

    /**
     * Stores task data as JSON (the starter's Jackson 3 serializer) instead of the Java serialization default: readable
     * in the table and tolerant of added fields.
     *
     * @return the customizer
     */
    @Bean
    DbSchedulerCustomizer jsonTaskData() {
        var serializer = new Jackson3Serializer();
        return new DbSchedulerCustomizer() {
            @Override
            public Optional<Serializer> serializer() {
                return Optional.of(serializer);
            }
        };
    }

    /**
     * Observes every execution.
     *
     * @param observations where executions are recorded
     * @return the interceptor
     */
    @Bean
    ObservedTaskExecution observedTaskExecution(ObservationRegistry observations) {
        return new ObservedTaskExecution(observations);
    }

    /**
     * Logs every failed execution once.
     *
     * @param properties the retry settings
     * @return the listener
     */
    @Bean
    TaskFailureLog taskFailureLog(SchedulingProperties properties) {
        return new TaskFailureLog(properties);
    }

    /**
     * Pauses picking while the application context is stopped.
     *
     * @param scheduler the application's scheduler
     * @return the lifecycle
     */
    @Bean
    SchedulerPausing schedulerPausing(Scheduler scheduler) {
        return new SchedulerPausing(scheduler);
    }
}
