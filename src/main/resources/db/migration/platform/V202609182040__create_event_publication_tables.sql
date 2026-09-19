-- Transactional outbox: the Spring Modulith 2.1.1 JDBC event publication registry (schema v2), copied from
-- org/springframework/modulith/events/jdbc/schemas/v2/schema-postgresql{,-archive}.sql and qualified with the
-- platform schema. Keep the columns identical to the official DDL; the registry's SQL depends on them.

-- Publications not yet completed (published, processing, failed or resubmitted).
create table platform.event_publication (
    id uuid not null,
    listener_id text not null,
    event_type text not null,
    serialized_event text not null,
    publication_date timestamp with time zone not null,
    completion_date timestamp with time zone,
    status text,
    completion_attempts int,
    last_resubmission_date timestamp with time zone,
    primary key (id)
);
create index event_publication_serialized_event_hash_idx
    on platform.event_publication using hash (serialized_event);
create index event_publication_by_completion_date_idx
    on platform.event_publication (completion_date);

-- Completed publications, moved here by completion-mode ARCHIVE once the listener (the NATS relay) succeeded.
create table platform.event_publication_archive (
    id uuid not null,
    listener_id text not null,
    event_type text not null,
    serialized_event text not null,
    publication_date timestamp with time zone not null,
    completion_date timestamp with time zone,
    status text,
    completion_attempts int,
    last_resubmission_date timestamp with time zone,
    primary key (id)
);
create index event_publication_archive_serialized_event_hash_idx
    on platform.event_publication_archive using hash (serialized_event);
create index event_publication_archive_by_completion_date_idx
    on platform.event_publication_archive (completion_date);
