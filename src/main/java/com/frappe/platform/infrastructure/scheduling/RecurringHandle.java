package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.RecurringTask;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedulingException;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceCurrentlyExecutingException;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceNotFoundException;
import com.github.kagkarlsson.scheduler.task.Task;
import com.github.kagkarlsson.scheduler.task.TaskInstanceId;
import com.github.kagkarlsson.shaded.jdbc.SQLRuntimeException;
import java.time.Clock;
import java.util.function.Supplier;

/**
 * A {@link RecurringTask} backed by a db-scheduler recurring task (instance {@value #INSTANCE}).
 *
 * @param <T> the data handed from one run to the next
 */
final class RecurringHandle<T> implements RecurringTask<T>, LibraryTask {

    /** Instance id of every recurring task (db-scheduler's {@code RecurringTask.INSTANCE}). */
    static final String INSTANCE = com.github.kagkarlsson.scheduler.task.helper.RecurringTask.INSTANCE;

    private final TaskName name;
    private final Task<T> task;
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
    RecurringHandle(TaskName name, Task<T> task, Supplier<Scheduler> scheduler, Clock clock) {
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

    // SchedulerClient.reschedule also resets the execution's consecutive failures and last success and failure, so a
    // failing task gets its fast retries again. A lost race (false) is logged by the library at WARN as well.
    @Override
    public boolean runNow(T nextData) {
        try {
            var moved =
                    scheduler.get().reschedule(TaskInstanceId.of(name.value(), INSTANCE), clock.instant(), nextData);
            if (moved) {
                scheduler.get().triggerCheckForDueExecutions();
            }
            return moved;
        } catch (TaskInstanceCurrentlyExecutingException | TaskInstanceNotFoundException e) {
            return false;
        } catch (SQLRuntimeException e) {
            throw new TaskSchedulingException(
                    "Moving the next run of " + name + " to now failed; its scheduled run still comes", e);
        }
    }
}
