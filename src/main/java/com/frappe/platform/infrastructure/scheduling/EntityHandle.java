package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.EntitySchedule;
import com.frappe.platform.EntityTask;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedulingException;
import com.github.kagkarlsson.scheduler.ScheduledExecution;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.SchedulerClient.ScheduleOptions;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceCurrentlyExecutingException;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceNotFoundException;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.Task;
import com.github.kagkarlsson.scheduler.task.TaskInstanceId;
import com.github.kagkarlsson.shaded.jdbc.SQLRuntimeException;
import java.time.Clock;
import java.time.Instant;
import java.util.Optional;
import java.util.function.Supplier;

/**
 * An {@link EntityTask} backed by a db-scheduler recurring task with a persistent schedule: one execution per entity,
 * its instance id the entity key, its data the {@link StoredEntitySchedule}.
 */
final class EntityHandle implements EntityTask, LibraryTask {

    private final TaskName name;
    private final Task<StoredEntitySchedule> task;
    private final Supplier<Scheduler> scheduler;
    private final Clock clock;

    /**
     * Creates the handle.
     *
     * @param name the task name
     * @param task the library task
     * @param scheduler the application's scheduler, looked up on use (it is built from the declared tasks)
     * @param clock the application clock
     */
    EntityHandle(TaskName name, Task<StoredEntitySchedule> task, Supplier<Scheduler> scheduler, Clock clock) {
        this.name = name;
        this.task = task;
        this.scheduler = scheduler;
        this.clock = clock;
    }

    @Override
    public TaskName name() {
        return name;
    }

    @Override
    public Task<?> libraryTask() {
        return task;
    }

    @Override
    public void schedule(String entityId, EntitySchedule schedule) {
        var stored = StoredEntitySchedule.of(schedule);
        var next = stored.getSchedule().getNextExecutionTime(ExecutionComplete.simulatedSuccess(clock.instant()));
        try {
            // false: the execution existed when inserting failed but was gone or changed when rescheduling it.
            if (!scheduler
                    .get()
                    .schedule(task.instance(entityId, stored), next, ScheduleOptions.WHEN_EXISTS_RESCHEDULE)) {
                throw new TaskSchedulingException(
                        name + " for '" + entityId + "' changed concurrently; set its schedule again");
            }
        } catch (TaskInstanceCurrentlyExecutingException e) {
            throw new TaskSchedulingException(
                    name + " for '" + entityId + "' is running; set its schedule again once the run ended", e);
        } catch (SQLRuntimeException e) {
            throw new TaskSchedulingException("Scheduling " + name + " for '" + entityId + "' failed", e);
        }
    }

    @Override
    public boolean cancel(String entityId) {
        try {
            scheduler.get().cancel(instanceId(entityId));
            return true;
        } catch (TaskInstanceNotFoundException e) {
            return false;
        } catch (TaskInstanceCurrentlyExecutingException e) {
            throw new TaskSchedulingException(
                    name + " for '" + entityId + "' is running; cancel it again once the run ended", e);
        } catch (SQLRuntimeException e) {
            throw new TaskSchedulingException("Cancelling " + name + " for '" + entityId + "' failed", e);
        }
    }

    @Override
    public Optional<Instant> nextRun(String entityId) {
        try {
            return scheduler
                    .get()
                    .getScheduledExecution(instanceId(entityId))
                    .map(ScheduledExecution::getExecutionTime);
        } catch (SQLRuntimeException e) {
            throw new TaskSchedulingException("Reading the next run of " + name + " for '" + entityId + "' failed", e);
        }
    }

    private TaskInstanceId instanceId(String entityId) {
        return TaskInstanceId.of(name.value(), entityId);
    }
}
