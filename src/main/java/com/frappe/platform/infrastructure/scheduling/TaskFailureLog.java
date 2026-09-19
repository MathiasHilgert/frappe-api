package com.frappe.platform.infrastructure.scheduling;

import com.github.kagkarlsson.scheduler.event.AbstractSchedulerListener;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete.Result;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Logs every failed task execution once, where its outcome is known: WARN while retries with backoff remain (it heals
 * itself), ERROR once they are used up (a human should look). The scheduler's own failure logging stays at its DEBUG
 * default, so this is the one line per failure, with the task name, instance and failure count as ECS fields.
 */
final class TaskFailureLog extends AbstractSchedulerListener {

    private static final Logger log = LoggerFactory.getLogger(TaskFailureLog.class);

    private final int maxRetries;

    /**
     * Creates the listener.
     *
     * @param properties the retry settings, to tell retried failures from exhausted ones
     */
    TaskFailureLog(SchedulingProperties properties) {
        this.maxRetries = properties.maxRetries();
    }

    /**
     * Logs the execution if it failed.
     *
     * @param executionComplete the finished execution
     */
    @Override
    public void onExecutionComplete(ExecutionComplete executionComplete) {
        if (executionComplete.getResult() != Result.FAILED) {
            return;
        }
        var execution = executionComplete.getExecution();
        // The execution still carries the count from before this failure.
        var failures = execution.consecutiveFailures + 1;
        var retriesLeft = failures <= maxRetries;
        (retriesLeft ? log.atWarn() : log.atError())
                .addKeyValue(LogFields.TASK_NAME, execution.taskInstance.getTaskName())
                .addKeyValue(LogFields.TASK_INSTANCE, execution.taskInstance.getId())
                .addKeyValue(LogFields.CONSECUTIVE_FAILURES, failures)
                .setCause(executionComplete.getCause().orElse(null))
                .log(
                        retriesLeft
                                ? "Scheduled task {} failed; retrying with exponential backoff"
                                : "Scheduled task {} keeps failing after its retries; recurring tasks continue on their"
                                        + " schedule, one-time tasks retry at the longest backoff",
                        execution.taskInstance.getTaskName());
    }
}
