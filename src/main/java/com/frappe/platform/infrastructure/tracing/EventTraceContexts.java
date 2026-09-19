package com.frappe.platform.infrastructure.tracing;

import io.micrometer.tracing.Tracer;
import io.micrometer.tracing.propagation.Propagator;
import java.time.Clock;
import java.util.HashMap;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataAccessException;
import org.springframework.stereotype.Component;

/**
 * The creation context of externalized events: the W3C trace context that was active when an event was recorded,
 * stored with the event so that every publish of it, resubmissions after an outage included, carries the original
 * context.
 */
@Component
public final class EventTraceContexts {

    private static final Logger log = LoggerFactory.getLogger(EventTraceContexts.class);

    private final Tracer tracer;
    private final Propagator propagator;
    private final EventTraceContextRepository repository;
    private final Clock clock;

    /**
     * Creates the component.
     *
     * @param tracer reads the trace context of the calling thread
     * @param propagator renders it in the configured propagation format
     * @param repository stores it
     * @param clock the application clock
     */
    EventTraceContexts(Tracer tracer, Propagator propagator, EventTraceContextRepository repository, Clock clock) {
        this.tracer = tracer;
        this.propagator = propagator;
        this.repository = repository;
        this.clock = clock;
    }

    /**
     * Stores the trace context active on the calling thread as the creation context of the event, in the caller's
     * transaction, so it commits and rolls back with the outbox row. Does nothing without an active trace, or when the
     * configured propagation does not produce a W3C {@code traceparent}.
     *
     * <p>The insert cannot fail for trace reasons (duplicates are ignored, values are validated); it fails only when
     * the database does, and then the outbox insert of the same transaction fails as well.
     *
     * @param eventId the event being recorded
     */
    public void recordCurrent(UUID eventId) {
        var current = tracer.currentTraceContext().context();
        if (current == null) {
            return;
        }
        // Rendered by the configured propagator, not by hand: it is the only source of the tracestate.
        Map<String, String> fields = new HashMap<>();
        propagator.inject(current, fields, Map::put);
        W3cTraceContext.parse(fields.get(W3cTraceContext.TRACEPARENT), fields.get(W3cTraceContext.TRACESTATE))
                .ifPresent(context -> repository.save(eventId, context, clock.instant()));
    }

    /**
     * The creation context of an event, for the transport that publishes it. Telemetry never fails a publish: if the
     * lookup fails, this logs one WARN and the event is published without trace headers.
     *
     * @param eventId the event being published
     * @return its creation context, or empty if it was recorded without a trace or the lookup failed
     */
    public Optional<W3cTraceContext> recordedFor(UUID eventId) {
        try {
            return repository.find(eventId);
        } catch (DataAccessException e) {
            log.atWarn()
                    .addKeyValue(LogFields.EVENT_ID, eventId)
                    .setCause(e)
                    .log("Reading the trace context of the event failed; publishing it without trace headers");
            return Optional.empty();
        }
    }
}
