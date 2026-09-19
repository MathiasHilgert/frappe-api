-- The W3C trace context an externalized event was recorded in (its creation context), written in the recording
-- transaction by the outbox adapter and read by the NATS relay on every publish, resubmissions included. Keyed by the
-- event id, not the publication id: one event has one creation context whatever number of listeners it has.
-- Rows live as long as the outbox history; they are purged together with platform.event_publication_archive.
create table platform.event_trace_context (
    event_id uuid not null,
    traceparent text not null,
    tracestate text not null,
    recorded_at timestamp with time zone not null,
    primary key (event_id)
);
