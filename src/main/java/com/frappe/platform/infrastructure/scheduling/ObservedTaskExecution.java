package com.frappe.platform.infrastructure.scheduling;

import com.github.kagkarlsson.scheduler.event.ExecutionChain;
import com.github.kagkarlsson.scheduler.event.ExecutionInterceptor;
import com.github.kagkarlsson.scheduler.task.CompletionHandler;
import com.github.kagkarlsson.scheduler.task.ExecutionContext;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;

/**
 * Observes every execution of every scheduled task as {@value #NAME}: a span and a timer tagged with the task name and
 * the outcome (both bounded), the instance id as a span attribute only. The task runs inside the observation, so its
 * queries and log lines join the trace. A failure is recorded as the observation's error and passed on unchanged: the
 * scheduler hands it to the task's {@link RetryingFailureHandler}, which logs it and retries with backoff.
 */
final class ObservedTaskExecution implements ExecutionInterceptor {

    /** Observation name of one task execution. */
    static final String NAME = "scheduled.task";

    /** Low-cardinality key: the task name. */
    static final String TASK_NAME = "scheduled.task.name";

    /** Low-cardinality key: {@value #SUCCESS} or {@value #FAILURE}. */
    static final String OUTCOME = "scheduled.task.outcome";

    /** High-cardinality key: the task instance id. */
    static final String TASK_INSTANCE = "scheduled.task.instance";

    /** Outcome of an execution that completed. */
    static final String SUCCESS = "success";

    /** Outcome of an execution that threw. */
    static final String FAILURE = "failure";

    private final ObservationRegistry observations;

    /**
     * Creates the interceptor.
     *
     * @param observations where executions are recorded
     */
    ObservedTaskExecution(ObservationRegistry observations) {
        this.observations = observations;
    }

    // Tagged as a failure up front and overwritten on success: Observation.observe records a thrown exception on the
    // observation and rethrows it, so a failing task is timed and tagged without catching anything here.
    @Override
    public CompletionHandler<?> execute(
            TaskInstance<?> taskInstance, ExecutionContext executionContext, ExecutionChain chain) {
        var observation = Observation.createNotStarted(NAME, observations)
                .contextualName("scheduled task " + taskInstance.getTaskName())
                .lowCardinalityKeyValue(TASK_NAME, taskInstance.getTaskName())
                .lowCardinalityKeyValue(OUTCOME, FAILURE)
                .highCardinalityKeyValue(TASK_INSTANCE, taskInstance.getId());
        return observation.observe(() -> {
            var completion = chain.proceed(taskInstance, executionContext);
            observation.lowCardinalityKeyValue(OUTCOME, SUCCESS);
            return completion;
        });
    }
}
