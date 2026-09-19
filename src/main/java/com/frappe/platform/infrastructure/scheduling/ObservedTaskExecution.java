package com.frappe.platform.infrastructure.scheduling;

import com.github.kagkarlsson.scheduler.event.ExecutionChain;
import com.github.kagkarlsson.scheduler.event.ExecutionInterceptor;
import com.github.kagkarlsson.scheduler.task.CompletionHandler;
import com.github.kagkarlsson.scheduler.task.ExecutionContext;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import io.micrometer.common.KeyValues;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationConvention;
import io.micrometer.observation.ObservationRegistry;

/**
 * Observes every execution of every scheduled task as {@value #NAME}: a span and a timer tagged with the task name and
 * the outcome (both bounded; the outcome is set when the run ends), the instance id as a span attribute only. The task runs inside the observation, so its
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

    // Observation.observe records a thrown exception on the observation and rethrows it, so a failing task is timed
    // and tagged without catching anything here. The outcome is decided by the convention when the observation stops;
    // while the task runs it has none, so the long task timer (scheduled.task.active) never reads a premature outcome.
    @Override
    public CompletionHandler<?> execute(
            TaskInstance<?> taskInstance, ExecutionContext executionContext, ExecutionChain chain) {
        var context = new TaskExecutionContext(taskInstance.getTaskName(), taskInstance.getId());
        return Observation.createNotStarted(TaskExecutionConvention.INSTANCE, () -> context, observations)
                .observe(() -> {
                    var completion = chain.proceed(taskInstance, executionContext);
                    context.completed = true;
                    return completion;
                });
    }

    /** One task execution: which task and instance, and whether the task returned. */
    static final class TaskExecutionContext extends Observation.Context {

        private final String taskName;
        private final String taskInstance;
        private boolean completed;

        /**
         * Creates the context.
         *
         * @param taskName the task name
         * @param taskInstance the instance id
         */
        TaskExecutionContext(String taskName, String taskInstance) {
            this.taskName = taskName;
            this.taskInstance = taskInstance;
        }
    }

    /**
     * Names and tags a task execution; applied when the observation starts and again when it stops, the only point
     * where the outcome is known.
     */
    static final class TaskExecutionConvention implements ObservationConvention<TaskExecutionContext> {

        /** The one convention; stateless. */
        static final TaskExecutionConvention INSTANCE = new TaskExecutionConvention();

        private TaskExecutionConvention() {}

        @Override
        public String getName() {
            return NAME;
        }

        @Override
        public String getContextualName(TaskExecutionContext context) {
            return "scheduled task " + context.taskName;
        }

        @Override
        public KeyValues getLowCardinalityKeyValues(TaskExecutionContext context) {
            var name = KeyValues.of(TASK_NAME, context.taskName);
            if (context.getError() != null) {
                return name.and(OUTCOME, FAILURE);
            }
            return context.completed ? name.and(OUTCOME, SUCCESS) : name;
        }

        @Override
        public KeyValues getHighCardinalityKeyValues(TaskExecutionContext context) {
            return KeyValues.of(TASK_INSTANCE, context.taskInstance);
        }

        @Override
        public boolean supportsContext(Observation.Context context) {
            return context instanceof TaskExecutionContext;
        }
    }
}
