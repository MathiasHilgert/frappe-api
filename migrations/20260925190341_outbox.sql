-- +goose Up
-- +goose StatementBegin
-- outbox holds integration events appended by use cases inside their
-- business transaction (internal/foundation/events/outbox) until the relay
-- publishes them to the broker.
--
-- id is text, not uuid: it is the CloudEvents event ID, which the
-- specification defines as an arbitrary non-empty string, and the
-- outbox.Store contract (storetest) does not restrict it to UUIDs.
-- position is a store-assigned sequence that breaks ties between messages
-- appended in the same transaction (same created_at and available_at), so
-- they are claimed in append order whatever their IDs look like.
-- payload is bytea, not jsonb: it is the encoded CloudEvents envelope and
-- must reach the broker byte for byte; jsonb would reorder keys, drop
-- duplicate keys and reformat whitespace (breaking signatures or digests
-- computed over the envelope) and would reject a non-JSON encoding.
-- created_at is when the message was appended (outbox.Pending.CreatedAt).
-- attempts counts failed publish attempts (outbox.Pending.Attempts).
-- Every timestamp is taken from the database clock (now()), never from
-- the application clock, so relay replicas on skewed hosts agree on
-- leases and retry times.
CREATE TABLE outbox (
    id text PRIMARY KEY,
    position bigint GENERATED ALWAYS AS IDENTITY,
    subject text NOT NULL,
    payload bytea NOT NULL,
    headers jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    available_at timestamptz NOT NULL DEFAULT now(),
    claimed_until timestamptz,
    attempts integer NOT NULL DEFAULT 0,
    last_error text NOT NULL DEFAULT '',
    published_at timestamptz
);
-- +goose StatementEnd

-- +goose StatementBegin
-- Serves Claim and OldestPending: only pending rows are indexed, so the
-- index stays small however many published rows await purging.
CREATE INDEX outbox_pending ON outbox (available_at, position) WHERE published_at IS NULL;
-- +goose StatementEnd

-- +goose StatementBegin
-- Serves Purge.
CREATE INDEX outbox_published ON outbox (published_at) WHERE published_at IS NOT NULL;
-- +goose StatementEnd

-- +goose StatementBegin
-- Wakes the relay up as soon as appended messages are committed: NOTIFY is
-- transactional, so listeners only hear it on commit, and a rolled back
-- business transaction notifies nobody. The payload is empty on purpose;
-- it is only a hint (the relay still polls) and carries no tenant data.
CREATE FUNCTION outbox_notify() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM pg_notify('outbox', '');
    RETURN NULL;
END;
$$;
-- +goose StatementEnd

-- +goose StatementBegin
CREATE TRIGGER outbox_notify AFTER INSERT ON outbox
    FOR EACH STATEMENT EXECUTE FUNCTION outbox_notify();
-- +goose StatementEnd

-- +goose StatementBegin
-- Row Level Security is deliberately NOT enabled on outbox. Rows are
-- written by frappe_application inside tenant-scoped transactions but are
-- never read through the API; the relay must read every tenant's rows.
-- Isolation is enforced by privileges instead: frappe_application may only
-- INSERT (it can neither read back nor alter any tenant's events), and only
-- frappe_outbox_relay may read, update and delete them. The default
-- privileges from 20260924000000_application_role_privileges.sql granted
-- frappe_application SELECT, UPDATE and DELETE too, so they are revoked
-- here. Append therefore never uses RETURNING, which needs SELECT.
REVOKE ALL ON outbox FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT INSERT ON outbox TO frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
-- Inserting into an identity column needs no privilege on its sequence.
REVOKE ALL ON SEQUENCE outbox_position_seq FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT USAGE ON SCHEMA public TO frappe_outbox_relay;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT SELECT, UPDATE, DELETE ON outbox TO frappe_outbox_relay;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DROP TABLE outbox;
-- +goose StatementEnd

-- +goose StatementBegin
DROP FUNCTION outbox_notify();
-- +goose StatementEnd

-- +goose StatementBegin
REVOKE USAGE ON SCHEMA public FROM frappe_outbox_relay;
-- +goose StatementEnd
