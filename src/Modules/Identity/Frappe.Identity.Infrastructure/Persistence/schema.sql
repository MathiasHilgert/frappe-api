
\restrict frappe

CREATE SCHEMA identity;

CREATE TABLE identity.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);

CREATE TABLE identity.email_change_undos (
    id uuid NOT NULL,
    principal_id uuid NOT NULL,
    old_address character varying(254) NOT NULL,
    new_address character varying(254) NOT NULL,
    token_hmac bytea NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    redeemed_at timestamp with time zone,
    invalidated_at timestamp with time zone,
    CONSTRAINT ck_email_change_undos_expires_after_issued CHECK ((expires_at > issued_at)),
    CONSTRAINT ck_email_change_undos_invalidated_after_issued CHECK (((invalidated_at IS NULL) OR (invalidated_at >= issued_at))),
    CONSTRAINT ck_email_change_undos_redeemed_in_window CHECK (((redeemed_at IS NULL) OR ((redeemed_at >= issued_at) AND (redeemed_at <= expires_at)))),
    CONSTRAINT ck_email_change_undos_token_hmac_length CHECK ((octet_length(token_hmac) = 32))
);

CREATE TABLE identity.email_identifiers (
    id uuid NOT NULL,
    principal_id uuid NOT NULL,
    address character varying(254) NOT NULL,
    normalized_address character varying(254) NOT NULL,
    verified_at timestamp with time zone
);

COMMENT ON COLUMN identity.email_identifiers.normalized_address IS 'DATA-12 copy: address trimmed and lower-cased; written with it by the Email value object, never updated alone.';

CREATE TABLE identity.inbox_messages (
    consumer character varying(100) NOT NULL,
    message_id uuid NOT NULL,
    received_at timestamp with time zone NOT NULL
);

CREATE TABLE identity.one_time_codes (
    id uuid NOT NULL,
    principal_id uuid NOT NULL,
    purpose character varying(18) NOT NULL,
    target character varying(254) NOT NULL,
    code_hmac bytea NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    redeemed_at timestamp with time zone,
    invalidated_at timestamp with time zone,
    CONSTRAINT ck_one_time_codes_code_hmac_length CHECK ((octet_length(code_hmac) = 32)),
    CONSTRAINT ck_one_time_codes_expires_after_issued CHECK ((expires_at > issued_at)),
    CONSTRAINT ck_one_time_codes_invalidated_after_issued CHECK (((invalidated_at IS NULL) OR (invalidated_at >= issued_at))),
    CONSTRAINT ck_one_time_codes_purpose CHECK (((purpose)::text = ANY ((ARRAY['email_verification'::character varying, 'password_reset'::character varying, 'email_change'::character varying])::text[]))),
    CONSTRAINT ck_one_time_codes_redeemed_in_window CHECK (((redeemed_at IS NULL) OR ((redeemed_at >= issued_at) AND (redeemed_at <= expires_at))))
);

CREATE TABLE identity.outbox_messages (
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

ALTER TABLE identity.outbox_messages ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity.outbox_messages_sequence_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity.owners (
    principal_id uuid NOT NULL,
    given_name character varying(200) NOT NULL,
    family_name character varying(200),
    locale character varying(35) NOT NULL,
    registered_at timestamp with time zone NOT NULL,
    verified_at timestamp with time zone,
    CONSTRAINT ck_owners_verified_after_registered CHECK ((verified_at >= registered_at))
);

CREATE TABLE identity.password_credentials (
    principal_id uuid NOT NULL,
    hash character varying(255) NOT NULL,
    changed_at timestamp with time zone NOT NULL
);

CREATE TABLE identity.principals (
    id uuid NOT NULL,
    kind character varying(5) NOT NULL,
    status character varying(7) NOT NULL,
    credential_stamp uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_principals_kind CHECK (((kind)::text = 'owner'::text)),
    CONSTRAINT ck_principals_status CHECK (((status)::text = ANY ((ARRAY['pending'::character varying, 'active'::character varying])::text[])))
);

CREATE TABLE identity.sessions (
    id uuid NOT NULL,
    principal_id uuid NOT NULL,
    token_hash bytea NOT NULL,
    audience character varying(7) NOT NULL,
    methods text[] NOT NULL,
    credential_stamp uuid NOT NULL,
    opened_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL,
    idle_expires_at timestamp with time zone NOT NULL,
    absolute_expires_at timestamp with time zone NOT NULL,
    ended_at timestamp with time zone,
    user_agent character varying(512),
    ip_prefix cidr,
    step_up_until timestamp with time zone,
    revoked_at timestamp with time zone,
    revocation_reason character varying(19),
    CONSTRAINT ck_sessions_audience CHECK (((audience)::text = 'console'::text)),
    CONSTRAINT ck_sessions_ended_after_revoked CHECK ((ended_at >= revoked_at)),
    CONSTRAINT ck_sessions_idle_within_absolute CHECK ((idle_expires_at <= absolute_expires_at)),
    CONSTRAINT ck_sessions_ip_prefix CHECK (((ip_prefix IS NULL) OR ((family((ip_prefix)::inet) = 4) AND (masklen((ip_prefix)::inet) = 24)) OR ((family((ip_prefix)::inet) = 6) AND (masklen((ip_prefix)::inet) = 48)))),
    CONSTRAINT ck_sessions_methods CHECK (((methods <@ ARRAY['pwd'::text]) AND (cardinality(methods) > 0))),
    CONSTRAINT ck_sessions_revocation_pair CHECK (((revoked_at IS NULL) = (revocation_reason IS NULL))),
    CONSTRAINT ck_sessions_revocation_reason CHECK (((revocation_reason)::text = ANY ((ARRAY['logout'::character varying, 'revoked_by_holder'::character varying, 'others_revoked'::character varying, 'password_changed'::character varying, 'password_reset'::character varying, 'email_changed'::character varying, 'email_change_undone'::character varying])::text[]))),
    CONSTRAINT ck_sessions_seen_after_opened CHECK ((opened_at <= last_seen_at)),
    CONSTRAINT ck_sessions_token_hash_length CHECK ((octet_length(token_hash) = 32))
);

ALTER TABLE ONLY identity.__ef_migrations_history
    ADD CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId");

ALTER TABLE ONLY identity.email_change_undos
    ADD CONSTRAINT pk_email_change_undos PRIMARY KEY (id);

ALTER TABLE ONLY identity.email_identifiers
    ADD CONSTRAINT pk_email_identifiers PRIMARY KEY (id);

ALTER TABLE ONLY identity.inbox_messages
    ADD CONSTRAINT pk_inbox_messages PRIMARY KEY (consumer, message_id);

ALTER TABLE ONLY identity.one_time_codes
    ADD CONSTRAINT pk_one_time_codes PRIMARY KEY (id);

ALTER TABLE ONLY identity.outbox_messages
    ADD CONSTRAINT pk_outbox_messages PRIMARY KEY (id);

ALTER TABLE ONLY identity.owners
    ADD CONSTRAINT pk_owners PRIMARY KEY (principal_id);

ALTER TABLE ONLY identity.password_credentials
    ADD CONSTRAINT pk_password_credentials PRIMARY KEY (principal_id);

ALTER TABLE ONLY identity.principals
    ADD CONSTRAINT pk_principals PRIMARY KEY (id);

ALTER TABLE ONLY identity.sessions
    ADD CONSTRAINT pk_sessions PRIMARY KEY (id);

CREATE INDEX ix_email_change_undos_expires_at ON identity.email_change_undos USING btree (expires_at);

CREATE INDEX ix_inbox_messages_received_at ON identity.inbox_messages USING btree (received_at);

CREATE INDEX ix_one_time_codes_expires_at ON identity.one_time_codes USING btree (expires_at);

CREATE INDEX ix_outbox_messages_parked ON identity.outbox_messages USING btree (sequence) WHERE ((published_at IS NULL) AND (attempts > 0) AND (next_attempt_at IS NULL));

CREATE INDEX ix_outbox_messages_published_at ON identity.outbox_messages USING btree (published_at) WHERE (published_at IS NOT NULL);

CREATE INDEX ix_outbox_messages_relayable ON identity.outbox_messages USING btree (sequence) WHERE ((published_at IS NULL) AND ((attempts = 0) OR (next_attempt_at IS NOT NULL)));

CREATE INDEX ix_owners_registered_at ON identity.owners USING btree (registered_at) WHERE (verified_at IS NULL);

CREATE INDEX ix_sessions_ended_at ON identity.sessions USING btree (ended_at) WHERE (ended_at IS NOT NULL);

CREATE INDEX ix_sessions_principal_id ON identity.sessions USING btree (principal_id);

CREATE UNIQUE INDEX ux_email_change_undos_live ON identity.email_change_undos USING btree (principal_id, old_address, new_address) WHERE ((redeemed_at IS NULL) AND (invalidated_at IS NULL));

CREATE UNIQUE INDEX ux_email_identifiers_normalized_address ON identity.email_identifiers USING btree (normalized_address);

CREATE UNIQUE INDEX ux_email_identifiers_principal_id ON identity.email_identifiers USING btree (principal_id);

CREATE UNIQUE INDEX ux_one_time_codes_live ON identity.one_time_codes USING btree (principal_id, purpose) WHERE ((redeemed_at IS NULL) AND (invalidated_at IS NULL));

CREATE UNIQUE INDEX ux_sessions_token_hash ON identity.sessions USING btree (token_hash);

ALTER TABLE ONLY identity.email_change_undos
    ADD CONSTRAINT fk_email_change_undos_principal_id FOREIGN KEY (principal_id) REFERENCES identity.principals(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity.email_identifiers
    ADD CONSTRAINT fk_email_identifiers_principal_id FOREIGN KEY (principal_id) REFERENCES identity.principals(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity.one_time_codes
    ADD CONSTRAINT fk_one_time_codes_principal_id FOREIGN KEY (principal_id) REFERENCES identity.principals(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity.owners
    ADD CONSTRAINT fk_owners_principal_id FOREIGN KEY (principal_id) REFERENCES identity.principals(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity.password_credentials
    ADD CONSTRAINT fk_password_credentials_principal_id FOREIGN KEY (principal_id) REFERENCES identity.principals(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity.sessions
    ADD CONSTRAINT fk_sessions_principal_id FOREIGN KEY (principal_id) REFERENCES identity.principals(id) ON DELETE CASCADE;

GRANT USAGE ON SCHEMA identity TO frappe_identity;
GRANT USAGE ON SCHEMA identity TO frappe_identity_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.email_change_undos TO frappe_identity;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.email_identifiers TO frappe_identity;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.inbox_messages TO frappe_identity;
GRANT SELECT,DELETE ON TABLE identity.inbox_messages TO frappe_identity_worker;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.one_time_codes TO frappe_identity;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.outbox_messages TO frappe_identity;
GRANT SELECT,INSERT,DELETE ON TABLE identity.outbox_messages TO frappe_identity_worker;

GRANT UPDATE(published_at) ON TABLE identity.outbox_messages TO frappe_identity_worker;

GRANT UPDATE(leased_until) ON TABLE identity.outbox_messages TO frappe_identity_worker;

GRANT UPDATE(attempts) ON TABLE identity.outbox_messages TO frappe_identity_worker;

GRANT UPDATE(next_attempt_at) ON TABLE identity.outbox_messages TO frappe_identity_worker;

GRANT UPDATE(last_error) ON TABLE identity.outbox_messages TO frappe_identity_worker;

GRANT ALL ON SEQUENCE identity.outbox_messages_sequence_seq TO frappe_identity;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.owners TO frappe_identity;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.password_credentials TO frappe_identity;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.principals TO frappe_identity;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE identity.sessions TO frappe_identity;

ALTER DEFAULT PRIVILEGES FOR ROLE frappe_owner IN SCHEMA identity GRANT ALL ON SEQUENCES TO frappe_identity;

ALTER DEFAULT PRIVILEGES FOR ROLE frappe_owner IN SCHEMA identity GRANT SELECT,INSERT,DELETE,UPDATE ON TABLES TO frappe_identity;

\unrestrict frappe

