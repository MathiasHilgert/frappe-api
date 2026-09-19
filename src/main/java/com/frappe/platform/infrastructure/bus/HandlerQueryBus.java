package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Query;
import com.frappe.platform.QueryBus;
import com.frappe.platform.QueryHandler;
import java.util.Objects;

/** Routes a query to its handler bean by the query's exact type; knows nothing about telemetry. */
final class HandlerQueryBus implements QueryBus {

    private final HandlerRegistry handlers;

    /**
     * Creates the bus.
     *
     * @param handlers the query handlers, keyed by query type
     */
    HandlerQueryBus(HandlerRegistry handlers) {
        this.handlers = handlers;
    }

    @Override
    public <R> R ask(Query<R> query) {
        Objects.requireNonNull(query, "query must not be null");
        // Safe: the registry keys each handler by the Q of QueryHandler<Q, R>, and Q extends Query<R>.
        @SuppressWarnings("unchecked")
        var handler = (QueryHandler<Query<R>, R>) handlers.handlerFor(query.getClass());
        return handler.handle(query);
    }
}
