-- Test-only stopgap for FAPI-5: the Spring Modulith JPA publication registry table, so the NATS relay
-- tests can run as frappe_app (no DDL). FAPI-6 replaces this with the real outbox migration; delete it then.
create table platform.event_publication (
    id uuid primary key,
    listener_id text not null,
    event_type text not null,
    serialized_event text not null,
    publication_date timestamp with time zone not null,
    completion_date timestamp with time zone,
    last_resubmission_date timestamp with time zone,
    status text,
    completion_attempts integer not null default 0
);
