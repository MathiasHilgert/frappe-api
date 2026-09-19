package com.frappe.platform.infrastructure.tracing;

import java.sql.Timestamp;
import java.time.Instant;
import java.util.UUID;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/** The creation contexts of externalized events in {@code platform.event_trace_context}, keyed by event id. */
@Repository
class EventTraceContextRepository {

    // An event is recorded once, but a caller may hand the same instance to the publisher twice: the first context
    // wins, and a duplicate never fails the business transaction.
    private static final String INSERT = """
            insert into platform.event_trace_context (event_id, traceparent, tracestate, recorded_at)
            values (?, ?, ?, ?)
            on conflict (event_id) do nothing
            """;

    private final JdbcClient jdbc;

    /**
     * Creates the repository.
     *
     * @param jdbc client on the application data source
     */
    EventTraceContextRepository(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    /**
     * Stores the creation context of an event in the caller's transaction.
     *
     * @param eventId the event the context belongs to
     * @param context the context the event was recorded in
     * @param recordedAt when the event was recorded
     */
    void save(UUID eventId, W3cTraceContext context, Instant recordedAt) {
        jdbc.sql(INSERT)
                .params(eventId, context.traceparent(), context.tracestate(), Timestamp.from(recordedAt))
                .update();
    }
}
