package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.EntitySchedule;
import com.frappe.platform.ScheduledTasks;
import com.github.kagkarlsson.scheduler.task.FailureHandler;
import com.github.kagkarlsson.scheduler.task.FailureHandler.OnFailureReschedule;
import com.github.kagkarlsson.scheduler.task.FailureHandler.OnFailureRescheduleUsingTaskDataSchedule;
import com.github.kagkarlsson.scheduler.task.FailureHandler.OnFailureRetryLater;
import com.github.kagkarlsson.scheduler.task.MaxRetriesExceededListener;
import com.github.kagkarlsson.scheduler.task.StateReturningExecutionHandler;
import com.github.kagkarlsson.scheduler.task.VoidExecutionHandler;
import com.github.kagkarlsson.scheduler.task.helper.OneTimeTask;
import com.github.kagkarlsson.scheduler.task.helper.RecurringTask;
import com.github.kagkarlsson.scheduler.task.helper.RecurringTaskWithPersistentSchedule;
import com.github.kagkarlsson.scheduler.task.helper.Tasks;
import com.github.kagkarlsson.scheduler.task.schedule.Schedule;
import java.util.regex.Pattern;

/**
 * {@link ScheduledTasks} over db-scheduler's {@link Tasks} builders: validates the name and applies the retry
 * convention of {@link SchedulingProperties} through the library's {@link FailureHandler#maxRetries} builder.
 */
final class ConventionalScheduledTasks implements ScheduledTasks {

    private static final Pattern TASK_NAME = Pattern.compile("[a-z][a-z0-9]*\\.[a-z0-9]+(-[a-z0-9]+)*");

    private static final double BACKOFF_MULTIPLIER = 2.0;

    // Every failure is already logged once by TaskFailureLog, at ERROR once the retries are used up.
    private static final MaxRetriesExceededListener LOGGED_BY_FAILURE_LOG = complete -> {};

    private final SchedulingProperties properties;

    /**
     * Creates the task factory.
     *
     * @param properties the retry settings
     */
    ConventionalScheduledTasks(SchedulingProperties properties) {
        this.properties = properties;
    }

    @Override
    public RecurringTask<Void> recurring(String name, Schedule schedule, VoidExecutionHandler<Void> handler) {
        return Tasks.recurring(checked(name), schedule)
                .onFailure(retryingThen(new OnFailureReschedule<>(schedule)))
                .execute(handler);
    }

    @Override
    public <T> RecurringTask<T> recurring(
            String name,
            Schedule schedule,
            Class<T> dataType,
            T initialData,
            StateReturningExecutionHandler<T> handler) {
        return Tasks.recurring(checked(name), schedule, dataType)
                .initialData(initialData)
                .onFailure(retryingThen(new OnFailureReschedule<>(schedule)))
                .executeStateful(handler);
    }

    @Override
    public <T> OneTimeTask<T> oneTime(String name, Class<T> dataType, VoidExecutionHandler<T> handler) {
        return Tasks.oneTime(checked(name), dataType)
                .onFailure(retryingThen(new OnFailureRetryLater<>(properties.longestBackoff())))
                .execute(handler);
    }

    @Override
    public RecurringTaskWithPersistentSchedule<EntitySchedule> perEntity(
            String name, VoidExecutionHandler<EntitySchedule> handler) {
        return Tasks.recurringWithPersistentSchedule(checked(name), EntitySchedule.class)
                .onFailure(retryingThen(new OnFailureRescheduleUsingTaskDataSchedule<>()))
                .execute(handler);
    }

    // Backoff first: most failures are transient (database failover, a dependency restarting). The library counts
    // consecutive failures per execution, so the fallback applies until the next success resets the count.
    private <T> FailureHandler<T> retryingThen(FailureHandler<T> afterRetries) {
        return FailureHandler.<T>maxRetries(properties.maxRetries())
                .withBackoff(properties.initialBackoff(), BACKOFF_MULTIPLIER)
                .then(afterRetries, LOGGED_BY_FAILURE_LOG);
    }

    private static String checked(String name) {
        if (name == null || !TASK_NAME.matcher(name).matches()) {
            throw new IllegalArgumentException("Scheduled task name '" + name
                    + "' must be <module>.<kebab-case-name>, e.g. platform.outbox-recovery");
        }
        return name;
    }
}
