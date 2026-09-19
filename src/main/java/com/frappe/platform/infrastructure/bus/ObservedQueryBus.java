package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Query;
import com.frappe.platform.QueryBus;
import java.util.Objects;

/** Decorates a query bus with one {@code use_case} observation per query; handlers never create telemetry. */
final class ObservedQueryBus implements QueryBus {

    private final QueryBus delegate;
    private final UseCaseObservations observations;

    /**
     * Creates the decorator.
     *
     * @param delegate the bus that runs the handler
     * @param observations records the query
     */
    ObservedQueryBus(QueryBus delegate, UseCaseObservations observations) {
        this.delegate = delegate;
        this.observations = observations;
    }

    @Override
    public <R> R ask(Query<R> query) {
        Objects.requireNonNull(query, "query must not be null");
        return observations.observe(UseCaseKind.QUERY, query, () -> delegate.ask(query));
    }
}
