package com.frappe.platform.infrastructure.bus;

import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import java.util.function.Supplier;

/**
 * Runs one dispatch inside its {@code use_case} observation. The observation wraps the call to the handler's proxy,
 * so a command's transaction begins and commits inside it: a failing commit is an {@code error}, never a
 * {@code success}.
 */
final class UseCaseObservations {

    private static final UseCaseObservationConvention CONVENTION = new UseCaseObservationConvention();

    private final ObservationRegistry registry;

    /**
     * Creates the observer.
     *
     * @param registry where the observations are recorded
     */
    UseCaseObservations(ObservationRegistry registry) {
        this.registry = registry;
    }

    /**
     * Observes one dispatch.
     *
     * @param kind command or query
     * @param message the dispatched message
     * @param dispatch runs the handler
     * @param <R> the result type
     * @return what the handler returned, unchanged
     */
    <R> R observe(UseCaseKind kind, Object message, Supplier<R> dispatch) {
        var context = new UseCaseObservationContext(UseCase.of(kind, message.getClass()));
        // observe(...) records any exception as the observation's error, rethrows it and stops in finally; the
        // convention reads the outcome from the context when the observation stops.
        return Observation.createNotStarted(null, CONVENTION, () -> context, registry)
                .observe(() -> {
                    var result = dispatch.get();
                    context.returned(result);
                    return result;
                });
    }
}
