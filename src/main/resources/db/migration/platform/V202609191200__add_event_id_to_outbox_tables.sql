-- FAPI-9: the archive purge and trace context retention need to match a stored publication to its DomainEvent
-- identity by equality, not by a LIKE scan of serialized_event (a table-sized scan per row, times three tables,
-- unindexable and unbounded at production volume). serialized_event is JSON written by Modulith's own EventSerializer
-- from the DomainEvent record; eventId is a mandatory component of every event (DomainEvent), so it is always present
-- under its record component name. A stored generated column keeps the value typed and indexable; Modulith's own
-- inserts name their columns explicitly (JdbcEventPublicationRepositoryV2), so an extra column never breaks them.
alter table platform.event_publication
    add column event_id uuid generated always as ((serialized_event::jsonb ->> 'eventId')::uuid) stored;
create index event_publication_event_id_idx on platform.event_publication (event_id);

alter table platform.event_publication_archive
    add column event_id uuid generated always as ((serialized_event::jsonb ->> 'eventId')::uuid) stored;
create index event_publication_archive_event_id_idx on platform.event_publication_archive (event_id);

alter table platform.event_publication_dead_letter
    add column event_id uuid generated always as ((serialized_event::jsonb ->> 'eventId')::uuid) stored;
create index event_publication_dead_letter_event_id_idx on platform.event_publication_dead_letter (event_id);
