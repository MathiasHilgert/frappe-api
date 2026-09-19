package com.frappe.platform;

/**
 * Scheduling or changing a task's run failed: the database could not be reached, the run is in progress and cannot be
 * changed now, or another instance changed it at the same moment. The message names the task and what to do.
 */
public class TaskSchedulingException extends RuntimeException {

    /**
     * Creates the exception for a refusal without an underlying failure.
     *
     * @param message what failed, naming the task
     */
    public TaskSchedulingException(String message) {
        super(message);
    }

    /**
     * Creates the exception.
     *
     * @param message what failed, naming the task
     * @param cause the underlying failure
     */
    public TaskSchedulingException(String message, Throwable cause) {
        super(message, cause);
    }
}
