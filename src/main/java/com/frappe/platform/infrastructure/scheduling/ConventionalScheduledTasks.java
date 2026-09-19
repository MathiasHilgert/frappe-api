package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.EntityTask;
import com.frappe.platform.OneTimeTask;
import com.frappe.platform.RecurringTask;
import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.task.FailureHandler;
import com.github.kagkarlsson.scheduler.task.FailureHandler.OnFailureReschedule;
import com.github.kagkarlsson.scheduler.task.FailureHandler.OnFailureRescheduleUsingTaskDataSchedule;
import com.github.kagkarlsson.scheduler.task.FailureHandler.OnFailureRetryLater;
import com.github.kagkarlsson.scheduler.task.MaxRetriesExceededListener;
import com.github.kagkarlsson.scheduler.task.helper.Tasks;
import java.time.Clock;
import java.util.function.Consumer;
import java.util.function.Supplier;
import java.util.function.UnaryOperator;

/**
 * {@link ScheduledTasks} over db-scheduler's {@link Tasks} builders: adapts the plain actions to the library's
 * handlers, translates the schedule and applies the retry convention of {@link SchedulingProperties}.
 */
final class ConventionalScheduledTasks implements ScheduledTasks {

    private static final double BACKOFF_MULTIPLIER = 2.0;

    // Every failure is already logged once by TaskFailureLog, at ERROR once the retries are used up.
    private static final MaxRetriesExceededListener LOGGED_BY_FAILURE_LOG = complete -> {};

    private final SchedulingProperties properties;
    private final Supplier<Scheduler> scheduler;
    private final Clock clock;

    /**
     * Creates the task factory.
     *
     * @param properties the retry settings
     * @param scheduler the application's scheduler, looked up when a task is scheduled: it is built from the tasks
     *     this factory creates
     * @param clock the application clock
     */
    ConventionalScheduledTasks(SchedulingProperties properties, Supplier<Scheduler> scheduler, Clock clock) {
        this.properties = properties;
        this.scheduler = scheduler;
        this.clock = clock;
    }

    @Override
    public RecurringTask<Void> recurring(TaskName name, TaskSchedule schedule, Runnable action) {
        var librarySchedule = LibrarySchedules.of(schedule);
        var task = Tasks.recurring(name.value(), librarySchedule)
                .onFailure(retryingThen(new OnFailureReschedule<>(librarySchedule)))
                .execute((instance, context) -> action.run());
        return new RecurringHandle<>(name, task, scheduler, clock);
    }

    @Override
    public <T> RecurringTask<T> recurring(
            TaskName name, TaskSchedule schedule, Class<T> dataType, T initialData, UnaryOperator<T> action) {
        var librarySchedule = LibrarySchedules.of(schedule);
        var task = Tasks.recurring(name.value(), librarySchedule, dataType)
                .initialData(initialData)
                .onFailure(retryingThen(new OnFailureReschedule<>(librarySchedule)))
                .executeStateful((instance, context) -> action.apply(instance.getData()));
        return new RecurringHandle<>(name, task, scheduler, clock);
    }

    @Override
    public <T> OneTimeTask<T> oneTime(TaskName name, Class<T> dataType, Consumer<T> action) {
        var task = Tasks.oneTime(name.value(), dataType)
                .onFailure(retryingThen(new OnFailureRetryLater<>(properties.longestBackoff())))
                .execute((instance, context) -> action.accept(instance.getData()));
        return new OneTimeHandle<>(name, task, scheduler);
    }

    @Override
    public EntityTask perEntity(TaskName name, Consumer<String> action) {
        var task = Tasks.recurringWithPersistentSchedule(name.value(), StoredEntitySchedule.class)
                .onFailure(retryingThen(new OnFailureRescheduleUsingTaskDataSchedule<>()))
                .execute((instance, context) -> action.accept(instance.getId()));
        return new EntityHandle(name, task, scheduler, clock);
    }

    // Backoff first: most failures are transient (database failover, a dependency restarting). The library counts
    // consecutive failures per execution, so the fallback applies until the next success resets the count.
    private <T> FailureHandler<T> retryingThen(FailureHandler<T> afterRetries) {
        return FailureHandler.<T>maxRetries(properties.maxRetries())
                .withBackoff(properties.initialBackoff(), BACKOFF_MULTIPLIER)
                .then(afterRetries, LOGGED_BY_FAILURE_LOG);
    }
}
