package com.frappe.platform.infrastructure.bus;

import io.micrometer.common.KeyValues;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationConvention;

/**
 * Names and tags the {@code use_case} observation: span {@code <module> <UseCase>}, timer {@code use_case}. Every key
 * is low cardinality (bounded by the code base), so it also tags the timer; message contents never become key values.
 */
final class UseCaseObservationConvention implements ObservationConvention<UseCaseObservationContext> {

    /** Name of the observation and of its timer. */
    static final String NAME = "use_case";

    /** Low-cardinality key: the simple name of the message type. */
    static final String USE_CASE_NAME = "use_case.name";

    /** Low-cardinality key: the module of the message type. */
    static final String USE_CASE_MODULE = "use_case.module";

    /** Low-cardinality key: {@code command} or {@code query}. */
    static final String USE_CASE_KIND = "use_case.kind";

    /** Low-cardinality key: {@code success}, {@code failure} or {@code error}. */
    static final String OUTCOME = "outcome";

    /** Creates the convention; stateless, one instance serves every dispatch. */
    UseCaseObservationConvention() {}

    @Override
    public boolean supportsContext(Observation.Context context) {
        return context instanceof UseCaseObservationContext;
    }

    @Override
    public String getName() {
        return NAME;
    }

    @Override
    public String getContextualName(UseCaseObservationContext context) {
        return context.useCase().module() + " " + context.useCase().name();
    }

    @Override
    public KeyValues getLowCardinalityKeyValues(UseCaseObservationContext context) {
        var useCase = context.useCase();
        return KeyValues.of(
                USE_CASE_NAME, useCase.name(),
                USE_CASE_MODULE, useCase.module(),
                USE_CASE_KIND, useCase.kind().tagValue(),
                OUTCOME, context.outcome().tagValue());
    }
}
