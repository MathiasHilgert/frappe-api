-- +goose Up
-- +goose StatementBegin
-- inbox remembers which events each consumer already processed
-- (internal/foundation/events/inbox), so a redelivered event is skipped.
-- A consumer handler records (consumer, event_id) with
-- INSERT ... ON CONFLICT DO NOTHING in the same transaction as its own
-- business writes, so both commit or roll back together.
--
-- event_id is text: it is the CloudEvents event ID (see the outbox
-- migration). processed_at is taken from the database clock and serves
-- the retention purge.
CREATE TABLE inbox (
    consumer text NOT NULL,
    event_id text NOT NULL,
    processed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (consumer, event_id)
);
-- +goose StatementEnd

-- +goose StatementBegin
-- Serves Purge.
CREATE INDEX inbox_processed_at ON inbox (processed_at);
-- +goose StatementEnd

-- +goose StatementBegin
-- Row Level Security is deliberately NOT enabled on inbox: it holds no
-- tenant data, only consumer names and opaque event IDs, and consumers run
-- outside any single tenant. Privileges are narrowed instead. The default
-- privileges granted frappe_application SELECT, INSERT, UPDATE and DELETE;
-- it keeps only what it needs:
--   INSERT for recording (ON CONFLICT DO NOTHING without a conflict target
--     and without RETURNING needs no SELECT),
--   DELETE plus SELECT on processed_at for the retention purge, whose
--     WHERE clause reads that column.
-- It can never read which events were processed nor rewrite a record.
REVOKE ALL ON inbox FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT INSERT, DELETE ON inbox TO frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT SELECT (processed_at) ON inbox TO frappe_application;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DROP TABLE inbox;
-- +goose StatementEnd
