package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.ScheduledTask;
import com.frappe.platform.ScheduledTasks;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.boot.autoconfigure.Jackson3Serializer;
import com.github.kagkarlsson.scheduler.boot.config.DbSchedulerConfigurationSupport;
import com.github.kagkarlsson.scheduler.boot.config.DbSchedulerCustomizer;
import com.github.kagkarlsson.scheduler.boot.config.DbSchedulerProperties;
import com.github.kagkarlsson.scheduler.serializer.Serializer;
import com.github.kagkarlsson.scheduler.stats.StatsRegistry;
import com.github.kagkarlsson.scheduler.task.Task;
import io.micrometer.observation.ObservationRegistry;
import java.time.Clock;
import java.util.List;
import java.util.Optional;
import javax.sql.DataSource;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.boot.sql.init.dependency.DependsOnDatabaseInitialization;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Builds the one db-scheduler {@link Scheduler} from the platform's declared {@link ScheduledTask} beans and the
 * {@code db-scheduler.*} settings, the way the starter does ({@link DbSchedulerConfigurationSupport}), because the
 * starter only collects the library's own task type, which modules never see. The starter still provides the settings,
 * metrics, health indicator and start at context-ready.
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
     * @param scheduler the application's scheduler; looked up lazily, since it is built from the declared tasks
     * @param clock the application clock
     * @return the conventions
     */
    @Bean
    ScheduledTasks scheduledTasks(SchedulingProperties properties, ObjectProvider<Scheduler> scheduler, Clock clock) {
        return new ConventionalScheduledTasks(properties, scheduler::getObject, clock);
    }

    /**
     * The application clock for the scheduler: due times, heartbeats and retry times follow the same clock as the rest
     * of the application. Replaces the starter's system clock.
     *
     * @param clock the application clock
     * @return the scheduler's clock
     */
    @Bean
    com.github.kagkarlsson.scheduler.Clock dbSchedulerClock(Clock clock) {
        return clock::instant;
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
     * The application's scheduler: every declared task, every execution observed, every failure logged. Started by
     * the starter once the context is ready ({@code db-scheduler.delay-startup-until-context-ready}).
     *
     * @param settings {@code db-scheduler.*}
     * @param customizer the JSON task data
     * @param stats the library's meters (the starter's Micrometer registry)
     * @param clock the scheduler's clock
     * @param dataSource the application's data source; wrapped transaction-aware, so scheduling joins transactions
     * @param tasks every declared task
     * @param observations where executions are recorded
     * @param properties the retry settings
     * @return the scheduler
     */
    @Bean(destroyMethod = "stop")
    @DependsOnDatabaseInitialization
    Scheduler scheduler(
            DbSchedulerProperties settings,
            DbSchedulerCustomizer customizer,
            StatsRegistry stats,
            com.github.kagkarlsson.scheduler.Clock clock,
            DataSource dataSource,
            List<ScheduledTask> tasks,
            ObservationRegistry observations,
            SchedulingProperties properties) {
        return DbSchedulerConfigurationSupport.buildScheduler(
                settings,
                customizer,
                stats,
                clock,
                dataSource,
                libraryTasks(tasks),
                List.of(new TaskFailureLog(properties)),
                List.of(new ObservedTaskExecution(observations)));
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

    private static List<Task<?>> libraryTasks(List<ScheduledTask> tasks) {
        return tasks.stream()
                .<Task<?>>map(task -> {
                    if (!(task instanceof LibraryTask library)) {
                        throw new IllegalStateException("Scheduled task " + task.name()
                                + " was not created by ScheduledTasks; declare it with ScheduledTasks");
                    }
                    return library.libraryTask();
                })
                .toList();
    }
}
