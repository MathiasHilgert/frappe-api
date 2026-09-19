package com.frappe.platform;

/**
 * Asks a {@link Query} of its single {@link QueryHandler} and returns the answer unchanged. Every query is observed
 * (span and {@code use_case} timer).
 */
public interface QueryBus {

    /**
     * Answers the query in the calling thread.
     *
     * @param query the query, not {@code null}
     * @param <R> the answer type the query declares
     * @return the answer of its handler
     * @throws NullPointerException if the query is {@code null}
     * @throws RuntimeException the handler's own exception, or a {@code MissingHandlerException} when no handler is
     *     declared for the query type (a programming error)
     */
    <R> R ask(Query<R> query);
}
