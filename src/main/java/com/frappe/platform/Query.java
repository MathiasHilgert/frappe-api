package com.frappe.platform;

/**
 * A request to read state without changing it: one record per use case, named for what it finds ({@code
 * FindOpenTabs}), asked through the {@link QueryBus} of exactly one {@link QueryHandler}.
 *
 * @param <R> what answering the query returns: a read model record, never an aggregate
 */
public interface Query<R> {}
