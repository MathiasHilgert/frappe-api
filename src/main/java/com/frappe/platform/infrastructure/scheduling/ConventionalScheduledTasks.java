package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.EntityTask;
import com.frappe.platform.OneTimeTask;
import com.frappe.platform.RecurringTask;
import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.helper.ScheduleAndData;
import com.github.kagkarlsson.scheduler.task.helper.Tasks;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Clock;
import java.time.Instant;
import java.util.function.Consumer;
import java.util.function.Supplier;
import java.util.function.UnaryOperator;

/**
 * {@link ScheduledTasks} over db-scheduler's {@link Tasks} builders: adapts the plain actions to the library's
 * handlers, translates the schedule and applies the retry convention ({@link RetryingFailureHandler}).
 */
final class ConventionalScheduledTasks implements ScheduledTasks {

    private final SchedulingProperties properties;
    private final Supplier<Scheduler> scheduler;
    private final Clock clock;
    private final MeterRegistry meters;

    /**
     * Creates the task factory.
     *
     * @param properties the retry settings
     * @param scheduler the application's scheduler, looked up when a task is scheduled: it is built from the tasks
     *     this factory creates
     * @param clock the application clock
     * @param meters where one-time tasks count their given-up runs
     */
    ConventionalScheduledTasks(
            SchedulingProperties properties, Supplier<Scheduler> scheduler, Clock clock, MeterRegistry meters) {
        this.properties = properties;
        this.scheduler = scheduler;
        this.clock = clock;
        this.meters = meters;
    }

    @Override
    public RecurringTask<Void> recurring(TaskName name, TaskSchedule schedule, Runnable action) {
        var librarySchedule = LibrarySchedules.of(schedule);
        var task = Tasks.recurring(name.value(), librarySchedule)
                .onFailure(RetryingFailureHandler.recurring(properties, librarySchedule::getNextExecutionTime))
                .execute((instance, context) -> action.run());
        return new RecurringHandle<>(name, task, scheduler, clock);
    }

    @Override
    public <T> RecurringTask<T> recurring(
            TaskName name, TaskSchedule schedule, Class<T> dataType, T initialData, UnaryOperator<T> action) {
        var librarySchedule = LibrarySchedules.of(schedule);
        var task = Tasks.recurring(name.value(), librarySchedule, dataType)
                .initialData(initialData)
                .onFailure(RetryingFailureHandler.recurring(properties, librarySchedule::getNextExecutionTime))
                .executeStateful((instance, context) -> action.apply(instance.getData()));
        return new RecurringHandle<>(name, task, scheduler, clock);
    }

    @Override
    public <T> OneTimeTask<T> oneTime(TaskName name, Class<T> dataType, Consumer<T> action) {
        var task = Tasks.oneTime(name.value(), dataType)
                .onFailure(RetryingFailureHandler.oneTime(properties, meters, name.value()))
                .execute((instance, context) -> action.accept(instance.getData()));
        return new OneTimeHandle<>(name, task, scheduler);
    }

    @Override
    public EntityTask perEntity(TaskName name, Consumer<String> action) {
        var task = Tasks.recurringWithPersistentSchedule(name.value(), StoredEntitySchedule.class)
                .onFailure(RetryingFailureHandler.recurring(properties, ConventionalScheduledTasks::entityScheduleNext))
                .execute((instance, context) -> action.accept(instance.getId()));
        return new EntityHandle(name, task, scheduler, clock);
    }

    // The entity's own schedule is the task data of its execution.
    private static Instant entityScheduleNext(ExecutionComplete complete) {
        var stored = (ScheduleAndData) complete.getExecution().taskInstance.getData();
        return stored.getSchedule().getNextExecutionTime(complete);
    }
}
