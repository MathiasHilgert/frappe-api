-- The W3C trace context an externalized event was recorded in (its creation context), written in the recording
-- transaction by the outbox adapter and read by the NATS relay on every publish, resubmissions included. Keyed by the
-- event id, not the publication id: one event has one creation context whatever number of listeners it has.
-- Retention: a row may be purged only once its event id is in none of platform.event_publication,
-- platform.event_publication_archive and platform.event_publication_dead_letter (matched by the eventId inside
-- serialized_event): a dead letter replayed by hand must still publish with its original context. The purge follows
-- the archive purge; it selects by that rule, not by age, so recorded_at needs no index.
create table platform.event_trace_context (
    event_id uuid not null,
    traceparent text not null,
    tracestate text not null,
    recorded_at timestamp with time zone not null,
    primary key (event_id)
);
