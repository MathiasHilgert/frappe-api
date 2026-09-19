package com.frappe.platform.infrastructure.scheduling;

import com.github.kagkarlsson.scheduler.task.Task;

/** A platform task backed by a db-scheduler task, which the scheduler is built with. */
interface LibraryTask {

    /**
     * The db-scheduler task behind the platform task.
     *
     * @return the library task
     */
    Task<?> libraryTask();
}
