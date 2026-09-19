package com.frappe.platform;

/**
 * Answers one {@link Query} type with a read model. Declaring a Spring bean that implements this interface is all it
 * takes; the bus finds it at startup by the query type.
 *
 * <p>Query handlers are not {@code @Transactional}: they read, and never save aggregates or record events. Handlers
 * never create telemetry: the bus observes every query.
 *
 * @param <Q> the answered query type, a concrete record
 * @param <R> the answer, the {@code R} of the query
 */
public interface QueryHandler<Q extends Query<R>, R> {

    /**
     * Answers one query.
     *
     * @param query the query, never {@code null}
     * @return the read model
     */
    R handle(Q query);
}
