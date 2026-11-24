
\restrict frappe

CREATE SCHEMA notifications;

CREATE TABLE notifications.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);

CREATE TABLE notifications.deliveries (
    id uuid NOT NULL,
    source_message_id uuid NOT NULL,
    kind character varying(100) NOT NULL,
    category character varying(13) NOT NULL,
    channel character varying(30) NOT NULL,
    recipient_key character varying(150) NOT NULL,
    address_hash bytea,
    hash_key_id smallint,
    locale_served character varying(35),
    status character varying(10) NOT NULL,
    failure_reason character varying(17),
    provider character varying(6),
    provider_message_id character varying(100),
    created_at timestamp with time zone NOT NULL,
    sent_at timestamp with time zone,
    delivered_at timestamp with time zone,
    status_changed_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_deliveries_address_hash_length CHECK (((address_hash IS NULL) OR (octet_length(address_hash) = 32))),
    CONSTRAINT ck_deliveries_address_hash_pair CHECK (((address_hash IS NULL) = (hash_key_id IS NULL))),
    CONSTRAINT ck_deliveries_category CHECK (((category)::text = ANY ((ARRAY['security'::character varying, 'transactional'::character varying, 'marketing'::character varying])::text[]))),
    CONSTRAINT ck_deliveries_failed_reason CHECK ((((status)::text = 'failed'::text) = (failure_reason IS NOT NULL))),
    CONSTRAINT ck_deliveries_failure_reason CHECK (((failure_reason)::text = ANY ((ARRAY['provider_rejected'::character varying, 'invalid_recipient'::character varying])::text[]))),
    CONSTRAINT ck_deliveries_kind_format CHECK (((kind)::text ~ '^[a-z][a-z0-9_]*$'::text)),
    CONSTRAINT ck_deliveries_provider CHECK (((provider)::text = 'resend'::text)),
    CONSTRAINT ck_deliveries_provider_pair CHECK (((provider IS NULL) = (provider_message_id IS NULL))),
    CONSTRAINT ck_deliveries_recipient_key_format CHECK (((recipient_key)::text ~ '^(ref:[a-z_]+:.+|addr:[0-9]+:[0-9a-f]{64})$'::text)),
    CONSTRAINT ck_deliveries_sent_like CHECK ((((status)::text <> ALL ((ARRAY['sent'::character varying, 'delivered'::character varying, 'delayed'::character varying, 'bounced'::character varying, 'complained'::character varying])::text[])) OR ((provider_message_id IS NOT NULL) AND (sent_at IS NOT NULL)))),
    CONSTRAINT ck_deliveries_sent_like_hash CHECK ((((status)::text <> ALL ((ARRAY['sent'::character varying, 'delivered'::character varying, 'delayed'::character varying, 'bounced'::character varying, 'complained'::character varying])::text[])) OR (address_hash IS NOT NULL))),
    CONSTRAINT ck_deliveries_status CHECK (((status)::text = ANY ((ARRAY['sent'::character varying, 'delivered'::character varying, 'delayed'::character varying, 'bounced'::character varying, 'complained'::character varying, 'failed'::character varying, 'unresolved'::character varying])::text[]))),
    CONSTRAINT ck_deliveries_status_delivered_at CHECK ((((status)::text <> 'delivered'::text) OR (delivered_at IS NOT NULL)))
);

COMMENT ON COLUMN notifications.deliveries.address_hash IS 'DATA-12 copy: the hash part of recipient_key (addr:<key id>:<hex>) for an address recipient; written with it, never updated.';

COMMENT ON COLUMN notifications.deliveries.hash_key_id IS 'DATA-12 copy: the key id part of recipient_key (addr:<key id>:<hex>) for an address recipient; written with it, never updated.';

CREATE TABLE notifications.inbox_messages (
    consumer character varying(100) NOT NULL,
    message_id uuid NOT NULL,
    received_at timestamp with time zone NOT NULL
);

CREATE TABLE notifications.outbox_messages (
    id uuid NOT NULL,
    sequence bigint NOT NULL,
    type character varying(200) NOT NULL,
    source character varying(200) NOT NULL,
    data_content_type character varying(100) NOT NULL,
    data jsonb NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    trace_parent character varying(55),
    trace_state character varying(512),
    published_at timestamp with time zone,
    leased_until timestamp with time zone,
    attempts integer NOT NULL,
    next_attempt_at timestamp with time zone,
    last_error character varying(2000),
    CONSTRAINT ck_outbox_messages_attempts_nonnegative CHECK ((attempts >= 0)),
    CONSTRAINT ck_outbox_messages_last_error_after_failure CHECK (((last_error IS NULL) OR (attempts > 0))),
    CONSTRAINT ck_outbox_messages_next_attempt_after_failure CHECK (((next_attempt_at IS NULL) OR (attempts > 0))),
    CONSTRAINT ck_outbox_messages_published_settled CHECK (((published_at IS NULL) OR ((leased_until IS NULL) AND (last_error IS NULL))))
);

ALTER TABLE notifications.outbox_messages ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME notifications.outbox_messages_sequence_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE notifications.preferences (
    recipient_key character varying(150) NOT NULL,
    category character varying(13) NOT NULL,
    opted_out_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_preferences_only_marketing CHECK (((category)::text = 'marketing'::text)),
    CONSTRAINT ck_preferences_recipient_key_format CHECK (((recipient_key)::text ~ '^(ref:[a-z_]+:.+|addr:[0-9]+:[0-9a-f]{64})$'::text))
);

CREATE TABLE notifications.provider_events (
    provider character varying(6) NOT NULL,
    event_id character varying(100) NOT NULL,
    received_at timestamp with time zone NOT NULL,
    matched boolean NOT NULL,
    provider_message_id character varying(100),
    feedback character varying(22),
    occurred_at timestamp with time zone,
    CONSTRAINT ck_provider_events_feedback CHECK (((feedback)::text = ANY ((ARRAY['delivered'::character varying, 'permanent_failure'::character varying, 'complaint'::character varying, 'delayed'::character varying, 'suppressed_by_provider'::character varying])::text[]))),
    CONSTRAINT ck_provider_events_parked_together CHECK ((((provider_message_id IS NULL) = (feedback IS NULL)) AND ((feedback IS NULL) = (occurred_at IS NULL)))),
    CONSTRAINT ck_provider_events_parked_unmatched CHECK (((NOT matched) OR (provider_message_id IS NULL))),
    CONSTRAINT ck_provider_events_provider CHECK (((provider)::text = 'resend'::text))
);

CREATE TABLE notifications.send_requests (
    id uuid NOT NULL,
    kind character varying(100) NOT NULL,
    requested_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_send_requests_kind_format CHECK (((kind)::text ~ '^[a-z][a-z0-9_]*$'::text))
);

CREATE TABLE notifications.suppressions (
    channel character varying(30) NOT NULL,
    address_hash bytea NOT NULL,
    hash_key_id smallint NOT NULL,
    reason character varying(20) NOT NULL,
    source character varying(7) NOT NULL,
    provider character varying(6),
    provider_event_id character varying(100),
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_suppressions_address_hash_length CHECK ((octet_length(address_hash) = 32)),
    CONSTRAINT ck_suppressions_provider CHECK (((provider)::text = 'resend'::text)),
    CONSTRAINT ck_suppressions_provider_pair CHECK (((provider IS NULL) = (provider_event_id IS NULL))),
    CONSTRAINT ck_suppressions_reason CHECK (((reason)::text = ANY ((ARRAY['hard_bounce'::character varying, 'complaint'::character varying, 'provider_suppression'::character varying])::text[]))),
    CONSTRAINT ck_suppressions_source CHECK (((source)::text = ANY ((ARRAY['webhook'::character varying, 'manual'::character varying])::text[])))
);

ALTER TABLE ONLY notifications.__ef_migrations_history
    ADD CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId");

ALTER TABLE ONLY notifications.deliveries
    ADD CONSTRAINT pk_deliveries PRIMARY KEY (id);

ALTER TABLE ONLY notifications.inbox_messages
    ADD CONSTRAINT pk_inbox_messages PRIMARY KEY (consumer, message_id);

ALTER TABLE ONLY notifications.outbox_messages
    ADD CONSTRAINT pk_outbox_messages PRIMARY KEY (id);

ALTER TABLE ONLY notifications.preferences
    ADD CONSTRAINT pk_preferences PRIMARY KEY (recipient_key, category);

ALTER TABLE ONLY notifications.provider_events
    ADD CONSTRAINT pk_provider_events PRIMARY KEY (provider, event_id);

ALTER TABLE ONLY notifications.send_requests
    ADD CONSTRAINT pk_send_requests PRIMARY KEY (id);

ALTER TABLE ONLY notifications.suppressions
    ADD CONSTRAINT pk_suppressions PRIMARY KEY (channel, address_hash);

CREATE INDEX ix_deliveries_created_at ON notifications.deliveries USING btree (created_at);

CREATE INDEX ix_inbox_messages_received_at ON notifications.inbox_messages USING btree (received_at);

CREATE INDEX ix_outbox_messages_parked ON notifications.outbox_messages USING btree (sequence) WHERE ((published_at IS NULL) AND (attempts > 0) AND (next_attempt_at IS NULL));

CREATE INDEX ix_outbox_messages_published_at ON notifications.outbox_messages USING btree (published_at) WHERE (published_at IS NOT NULL);

CREATE INDEX ix_outbox_messages_relayable ON notifications.outbox_messages USING btree (sequence) WHERE ((published_at IS NULL) AND ((attempts = 0) OR (next_attempt_at IS NOT NULL)));

CREATE INDEX ix_provider_events_provider_message ON notifications.provider_events USING btree (provider, provider_message_id) WHERE (provider_message_id IS NOT NULL);

CREATE INDEX ix_provider_events_received_at ON notifications.provider_events USING btree (received_at);

CREATE INDEX ix_send_requests_requested_at ON notifications.send_requests USING btree (requested_at);

CREATE UNIQUE INDEX ux_deliveries_message_kind_recipient_channel ON notifications.deliveries USING btree (source_message_id, kind, recipient_key, channel) NULLS NOT DISTINCT;

CREATE UNIQUE INDEX ux_deliveries_provider_message ON notifications.deliveries USING btree (provider, provider_message_id) WHERE (provider_message_id IS NOT NULL);

GRANT USAGE ON SCHEMA notifications TO frappe_notifications;
GRANT USAGE ON SCHEMA notifications TO frappe_notifications_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE notifications.deliveries TO frappe_notifications;
GRANT SELECT,INSERT,DELETE ON TABLE notifications.deliveries TO frappe_notifications_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE notifications.inbox_messages TO frappe_notifications;
GRANT SELECT,INSERT,DELETE ON TABLE notifications.inbox_messages TO frappe_notifications_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE notifications.outbox_messages TO frappe_notifications;
GRANT SELECT,DELETE ON TABLE notifications.outbox_messages TO frappe_notifications_worker;

GRANT UPDATE(published_at) ON TABLE notifications.outbox_messages TO frappe_notifications_worker;

GRANT UPDATE(leased_until) ON TABLE notifications.outbox_messages TO frappe_notifications_worker;

GRANT UPDATE(attempts) ON TABLE notifications.outbox_messages TO frappe_notifications_worker;

GRANT UPDATE(next_attempt_at) ON TABLE notifications.outbox_messages TO frappe_notifications_worker;

GRANT UPDATE(last_error) ON TABLE notifications.outbox_messages TO frappe_notifications_worker;

GRANT ALL ON SEQUENCE notifications.outbox_messages_sequence_seq TO frappe_notifications;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE notifications.preferences TO frappe_notifications;
GRANT SELECT ON TABLE notifications.preferences TO frappe_notifications_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE notifications.provider_events TO frappe_notifications;
GRANT DELETE ON TABLE notifications.provider_events TO frappe_notifications_worker;

GRANT SELECT(provider) ON TABLE notifications.provider_events TO frappe_notifications_worker;

GRANT SELECT(received_at) ON TABLE notifications.provider_events TO frappe_notifications_worker;

GRANT SELECT(provider_message_id) ON TABLE notifications.provider_events TO frappe_notifications_worker;

GRANT SELECT(feedback) ON TABLE notifications.provider_events TO frappe_notifications_worker;

GRANT SELECT(occurred_at) ON TABLE notifications.provider_events TO frappe_notifications_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE notifications.send_requests TO frappe_notifications;
GRANT SELECT,DELETE ON TABLE notifications.send_requests TO frappe_notifications_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE notifications.suppressions TO frappe_notifications;
GRANT SELECT ON TABLE notifications.suppressions TO frappe_notifications_worker;

ALTER DEFAULT PRIVILEGES FOR ROLE frappe_owner IN SCHEMA notifications GRANT ALL ON SEQUENCES TO frappe_notifications;

ALTER DEFAULT PRIVILEGES FOR ROLE frappe_owner IN SCHEMA notifications GRANT SELECT,INSERT,DELETE,UPDATE ON TABLES TO frappe_notifications;

\unrestrict frappe

