package com.frappe.platform.infrastructure.scheduling;

import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.ExecutionOperations;
import com.github.kagkarlsson.scheduler.task.FailureHandler;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Instant;
import java.util.function.Function;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.event.Level;

/**
 * The platform's retry convention for every task, and the one log line per failed run. A failed run is retried after
 * {@code initial-backoff × 2^(failures - 1)} while {@code max-retries} remain (WARN). Afterwards a recurring task
 * continues on its schedule (ERROR on every further failure, until a success resets the count), and a one-time task
 * ends: its execution is removed, logged once at ERROR and counted as {@value #EXHAUSTED}.
 *
 * <p>Written instead of db-scheduler's {@code FailureHandler.maxRetries} builder, which cannot cap a recurring task's
 * backoff at its next regular run and leaves the logging to a listener that cannot tell the task kinds apart.
 *
 * @param <T> the task data
 */
final class RetryingFailureHandler<T> implements FailureHandler<T> {

    /** Counter of one-time runs given up after their retries, tagged with the task name. */
    static final String EXHAUSTED = "scheduled.task.exhausted";

    private static final Logger log = LoggerFactory.getLogger(RetryingFailureHandler.class);

    private final SchedulingProperties properties;
    // Recurring tasks: the next regular run after this one; null for one-time tasks, which end instead.
    private final Function<ExecutionComplete, Instant> regularNext;
    private final Counter exhausted;

    private RetryingFailureHandler(
            SchedulingProperties properties, Function<ExecutionComplete, Instant> regularNext, Counter exhausted) {
        this.properties = properties;
        this.regularNext = regularNext;
        this.exhausted = exhausted;
    }

    /**
     * The convention for a recurring task: retries never wait past its next regular run, and once they are used up it
     * continues on its schedule.
     *
     * @param <T> the task data
     * @param properties the retry settings
     * @param regularNext the next regular run after a failed one
     * @return the failure handler
     */
    static <T> RetryingFailureHandler<T> recurring(
            SchedulingProperties properties, Function<ExecutionComplete, Instant> regularNext) {
        return new RetryingFailureHandler<>(properties, regularNext, null);
    }

    /**
     * The convention for a one-time task: once its retries are used up it ends and is counted. The counter is
     * registered here, so it reads zero before the first give-up.
     *
     * @param <T> the task data
     * @param properties the retry settings
     * @param meters where the {@value #EXHAUSTED} counter is registered
     * @param taskName the task name, the counter's only tag (bounded: one per declared task)
     * @return the failure handler
     */
    static <T> RetryingFailureHandler<T> oneTime(
            SchedulingProperties properties, MeterRegistry meters, String taskName) {
        var exhausted = Counter.builder(EXHAUSTED)
                .description("One-time scheduled task runs given up after their retries")
                .tag(ObservedTaskExecution.TASK_NAME, taskName)
                .register(meters);
        return new RetryingFailureHandler<>(properties, null, exhausted);
    }

    @Override
    public void onFailure(ExecutionComplete complete, ExecutionOperations<T> operations) {
        // The execution still carries the count from before this failure.
        var failures = complete.getExecution().consecutiveFailures + 1;
        if (failures <= properties.maxRetries()) {
            log(Level.WARN, complete, failures, "Scheduled task {} failed; retrying with exponential backoff");
            operations.reschedule(complete, retryAt(complete, failures));
        } else if (regularNext != null) {
            log(
                    Level.ERROR,
                    complete,
                    failures,
                    "Scheduled task {} keeps failing after its retries; it continues on" + " its schedule");
            operations.reschedule(complete, regularNext.apply(complete));
        } else {
            log(
                    Level.ERROR,
                    complete,
                    failures,
                    "Scheduled task {} failed after its retries and was given up;"
                            + " schedule it again once the cause is fixed");
            operations.remove();
            exhausted.increment();
        }
    }

    private Instant retryAt(ExecutionComplete complete, int failures) {
        var backoff = complete.getTimeDone().plus(properties.initialBackoff().multipliedBy(1L << (failures - 1)));
        if (regularNext == null) {
            return backoff;
        }
        var regular = regularNext.apply(complete);
        return backoff.isBefore(regular) ? backoff : regular;
    }

    private static void log(Level level, ExecutionComplete complete, int failures, String message) {
        var instance = complete.getExecution().taskInstance;
        log.atLevel(level)
                .addKeyValue(LogFields.TASK_NAME, instance.getTaskName())
                .addKeyValue(LogFields.TASK_INSTANCE, instance.getId())
                .addKeyValue(LogFields.CONSECUTIVE_FAILURES, failures)
                .setCause(complete.getCause().orElse(null))
                .log(message, instance.getTaskName());
    }
}
