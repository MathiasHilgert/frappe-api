package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Result;
import io.micrometer.observation.Observation;
import java.util.Locale;
import org.jspecify.annotations.Nullable;

/** The state of one dispatch as the {@code use_case} observation sees it: which use case, and how it ended. */
final class UseCaseObservationContext extends Observation.Context {

    private final UseCase useCase;
    private @Nullable Object result;
    private boolean returned;

    /**
     * Creates the context of one dispatch.
     *
     * @param useCase the dispatched use case
     */
    UseCaseObservationContext(UseCase useCase) {
        this.useCase = useCase;
    }

    /**
     * The dispatched use case.
     *
     * @return the use case
     */
    UseCase useCase() {
        return useCase;
    }

    /**
     * Records what the handler returned.
     *
     * @param result the handler's return value
     */
    void returned(@Nullable Object result) {
        this.result = result;
        this.returned = true;
    }

    /**
     * How the dispatch ended so far: {@code error} once an exception was recorded, {@code failure} for a returned
     * {@link Result.Failure} (an expected business refusal), {@code success} for any other returned value, and
     * {@code unknown} while the handler still runs.
     *
     * @return the outcome
     */
    Outcome outcome() {
        if (getError() != null) {
            return Outcome.ERROR;
        }
        if (!returned) {
            return Outcome.UNKNOWN;
        }
        return result instanceof Result.Failure<?, ?> ? Outcome.FAILURE : Outcome.SUCCESS;
    }

    /** The values of the {@code outcome} tag. */
    enum Outcome {

        /** The handler returned a value that is not a failure. */
        SUCCESS,

        /** The handler returned a {@link Result.Failure}. */
        FAILURE,

        /** The handler, the transaction or the routing threw. */
        ERROR,

        /** The dispatch has not ended yet. */
        UNKNOWN;

        /**
         * The tag value.
         *
         * @return the lowercase name
         */
        String tagValue() {
            return name().toLowerCase(Locale.ROOT);
        }
    }
}
