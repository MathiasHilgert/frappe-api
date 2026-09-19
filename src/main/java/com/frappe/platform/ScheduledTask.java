package com.frappe.platform;

/**
 * A declared scheduled task. Created by {@link ScheduledTasks} and declared as a bean, which is how the scheduler
 * finds it.
 */
public interface ScheduledTask {

    /**
     * The task's name.
     *
     * @return the name
     */
    TaskName name();
}
