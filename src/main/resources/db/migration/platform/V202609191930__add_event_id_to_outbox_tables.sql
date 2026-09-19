-- FAPI-9: the archive purge and trace context retention need to match a stored publication to its DomainEvent
-- identity by equality, not by a LIKE scan of serialized_event (a table-sized scan per row, times three tables,
-- unindexable and unbounded at production volume). serialized_event is JSON written by Modulith's own EventSerializer
-- from the DomainEvent record; eventId is a mandatory component of every event (DomainEvent), so in every path this
-- application controls it is always present, a valid UUID, under its record component name. Modulith's own inserts
-- name their columns explicitly (JdbcEventPublicationRepositoryV2), so an extra generated column never breaks them.
--
-- platform.safe_event_id never lets a stored publication break its own insert (and so roll back the business
-- transaction that wrote it): a generated column expression cannot use PL/pgSQL exception handling directly, so the
-- cast is wrapped in this small IMMUTABLE function, which returns null instead of raising for a row that predates
-- this migration, was written by a future Modulith version with a different envelope, or was inserted or edited by
-- hand with a malformed payload (invalid JSON, or a non-UUID/missing eventId) — never for a row this application
-- itself produces today, but the guarantee is unconditional, not just today's happy path.
create function platform.safe_event_id(serialized_event text)
    returns uuid
    language plpgsql
    immutable
as $$
begin
    return (serialized_event::jsonb ->> 'eventId')::uuid;
exception
    when others then
        return null;
end;
$$;

alter table platform.event_publication
    add column event_id uuid generated always as (platform.safe_event_id(serialized_event)) stored;
create index event_publication_event_id_idx on platform.event_publication (event_id);

alter table platform.event_publication_archive
    add column event_id uuid generated always as (platform.safe_event_id(serialized_event)) stored;
create index event_publication_archive_event_id_idx on platform.event_publication_archive (event_id);

alter table platform.event_publication_dead_letter
    add column event_id uuid generated always as (platform.safe_event_id(serialized_event)) stored;
create index event_publication_dead_letter_event_id_idx on platform.event_publication_dead_letter (event_id);
