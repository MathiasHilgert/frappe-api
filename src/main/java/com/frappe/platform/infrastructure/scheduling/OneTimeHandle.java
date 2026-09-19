package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.OneTimeTask;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedulingException;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.task.Task;
import com.github.kagkarlsson.shaded.jdbc.SQLRuntimeException;
import java.time.Instant;
import java.util.function.Supplier;

/**
 * A {@link OneTimeTask} backed by a db-scheduler one-time task; the key is the instance id.
 *
 * @param <T> the instance data
 */
final class OneTimeHandle<T> implements OneTimeTask<T>, LibraryTask {

    private final TaskName name;
    private final Task<T> task;
    private final Supplier<Scheduler> scheduler;

    /**
     * Creates the handle.
     *
     * @param name the task name
     * @param task the library task
     * @param scheduler the application's scheduler, looked up on use (it is built from the declared tasks)
     */
    OneTimeHandle(TaskName name, Task<T> task, Supplier<Scheduler> scheduler) {
        this.name = name;
        this.task = task;
        this.scheduler = scheduler;
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
    public boolean schedule(String key, T data, Instant at) {
        try {
            return scheduler.get().scheduleIfNotExists(task.instance(key, data), at);
        } catch (SQLRuntimeException e) {
            throw new TaskSchedulingException("Scheduling " + name + " '" + key + "' failed; schedule it again", e);
        }
    }
}
