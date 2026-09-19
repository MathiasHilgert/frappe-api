package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalStateException;

import com.github.kagkarlsson.scheduler.event.ExecutionChain;
import com.github.kagkarlsson.scheduler.task.CompletionHandler;
import com.github.kagkarlsson.scheduler.task.ExecutionHandler;
import com.github.kagkarlsson.scheduler.task.TaskInstance;
import io.micrometer.observation.Observation;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.util.List;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;

class ObservedTaskExecutionTest {

    static final TaskInstance<Void> INSTANCE = new TaskInstance<>("platform.probe", "branch-42");

    final TestObservationRegistry observations = TestObservationRegistry.create();

    final ObservedTaskExecution interceptor = new ObservedTaskExecution(observations);

    @Test
    void observesASuccessfulExecutionWithTaskNameAndOutcome() {
        // Given
        CompletionHandler<Void> completion = new CompletionHandler.OnCompleteRemove<>();

        // When
        var returned = interceptor.execute(INSTANCE, null, chainRunning((instance, context) -> completion));

        // Then
        assertThat(returned).isSameAs(completion);
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("scheduled.task")
                .hasContextualNameEqualTo("scheduled task platform.probe")
                .hasLowCardinalityKeyValue("scheduled.task.name", "platform.probe")
                .hasLowCardinalityKeyValue("scheduled.task.outcome", "success")
                .hasHighCardinalityKeyValue("scheduled.task.instance", "branch-42")
                .doesNotHaveError()
                .hasBeenStopped();
    }

    @Test
    void observesAFailedExecutionAsAnErrorAndLetsTheSchedulerHandleIt() {
        // Given
        var failure = new IllegalStateException("database unavailable");

        // When / Then
        assertThatIllegalStateException()
                .isThrownBy(() -> interceptor.execute(INSTANCE, null, chainRunning((instance, context) -> {
                    throw failure;
                })))
                .isSameAs(failure);
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasLowCardinalityKeyValue("scheduled.task.outcome", "failure")
                .hasError(failure)
                .hasBeenStopped();
    }

    @Test
    void theTaskRunsInsideTheObservationSoItsQueriesAndLogsJoinTheTrace() {
        // Given
        var current = new AtomicReference<Observation>();

        // When
        interceptor.execute(INSTANCE, null, chainRunning((instance, context) -> {
            current.set(observations.getCurrentObservation());
            return new CompletionHandler.OnCompleteRemove<>();
        }));

        // Then
        assertThat(current.get()).isNotNull();
        assertThat(current.get().getContext().getName()).isEqualTo("scheduled.task");
    }

    @Test
    void whileTheTaskRunsItsObservationCarriesNoOutcomeSoTheActiveTimerDoesNotReadFailure() {
        // Given
        var outcomeWhileRunning = new AtomicReference<Object>("unset");

        // When
        interceptor.execute(INSTANCE, null, chainRunning((instance, context) -> {
            outcomeWhileRunning.set(observations
                    .getCurrentObservation()
                    .getContext()
                    .getLowCardinalityKeyValue("scheduled.task.outcome"));
            return new CompletionHandler.OnCompleteRemove<>();
        }));

        // Then
        assertThat(outcomeWhileRunning.get()).isNull();
    }

    private static ExecutionChain chainRunning(ExecutionHandler<Void> handler) {
        return new ExecutionChain(List.of(), handler);
    }
}
