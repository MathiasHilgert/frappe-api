-- Dead letters of the transactional outbox: publications the recovery job gave up on (attempts exhausted, event type
-- no longer on the classpath, unreadable payload). Same columns as platform.event_publication, so a row can be moved
-- back for a manual replay (see writing-code/references/domain-events.md), plus when and why it was given up.
create table platform.event_publication_dead_letter (
    id uuid not null,
    listener_id text not null,
    event_type text not null,
    serialized_event text not null,
    publication_date timestamp with time zone not null,
    completion_date timestamp with time zone,
    status text,
    completion_attempts int,
    last_resubmission_date timestamp with time zone,
    dead_lettered_at timestamp with time zone not null,
    reason text not null,
    primary key (id)
);
create index event_publication_dead_letter_by_dead_lettered_at_idx
    on platform.event_publication_dead_letter (dead_lettered_at);
