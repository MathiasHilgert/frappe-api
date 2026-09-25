-- +goose Up
-- +goose StatementBegin
-- locales lists every locale the platform may store user-entered
-- translations in (internal/foundation/i18n/localizedtext). It mirrors
-- the default I18N_SUPPORTED_LOCALES; supporting a new locale therefore
-- needs a migration inserting it here, and the foreign keys below reject
-- any text or translation in a locale nobody declared. Codes are canonical
-- BCP 47 tags, exactly as i18n.Locale.String() prints them.
CREATE TABLE locales (
    code text PRIMARY KEY
);
-- +goose StatementEnd

-- +goose StatementBegin
INSERT INTO locales (code) VALUES
    ('es-419'), ('en'), ('pt-BR'), ('fr'), ('it'),
    ('de'), ('ru'), ('zh-Hans'), ('ko'), ('ja');
-- +goose StatementEnd

-- +goose StatementBegin
-- The application only reads locales; they change through migrations.
REVOKE ALL ON locales FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT SELECT ON locales TO frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
-- localized_texts holds one user-entered, translatable string, such as a
-- dish description. Entities never get their own translation table:
-- they reference a text with a "<field>_text_id uuid REFERENCES
-- localized_texts (id)" column instead.
--
-- tenant_id is filled from the transaction's application.tenant setting
-- (see internal/foundation/database), so callers never pass it and can
-- never forge it. It is NULL for a global text (platform-wide content
-- such as a business type description): global texts are readable by
-- every tenant but written only by frappe_migration (see the policies
-- below), never by the application role.
-- source_hash is the hex SHA-256 of source_locale and source_value (see
-- localizedtext.Hash); a translation whose source_hash differs is stale.
-- context is an optional hint for machine translation; when NULL the
-- default context declared by the referencing field applies.
CREATE TABLE localized_texts (
    id uuid PRIMARY KEY,
    tenant_id text DEFAULT nullif(current_setting('application.tenant', true), '')
        CHECK (tenant_id <> ''),
    source_locale text NOT NULL REFERENCES locales (code),
    source_value text NOT NULL CHECK (source_value <> ''),
    source_hash text NOT NULL,
    context text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
-- +goose StatementEnd

-- +goose StatementBegin
-- Serves the orphan reaper, which scans one tenant's oldest texts.
CREATE INDEX localized_texts_tenant_updated_at ON localized_texts (tenant_id, updated_at);
-- +goose StatementEnd

-- +goose StatementBegin
-- localized_text_translations holds one translation of a text into one
-- locale. origin says who wrote it: a person (manual) or the machine
-- translator (machine); a machine translation never overwrites a manual
-- one. status is current, stale (the source changed after it was
-- translated: source_hash differs from the text's) or pending (a machine
-- translation was requested; value is NULL until it arrives, or keeps the
-- previous machine value while it is regenerated). requested_at is when
-- a machine translation was last requested and attempts how many times:
-- a pending row older than the pending timeout was lost and is requested
-- again (attempts lets the translator give up or alert). Deleting a text
-- deletes its translations.
CREATE TABLE localized_text_translations (
    text_id uuid NOT NULL REFERENCES localized_texts (id) ON DELETE CASCADE,
    locale text NOT NULL REFERENCES locales (code),
    value text CHECK (value <> ''),
    origin text NOT NULL CHECK (origin IN ('manual', 'machine')),
    status text NOT NULL CHECK (status IN ('current', 'stale', 'pending')),
    source_hash text NOT NULL,
    translated_at timestamptz NOT NULL DEFAULT now(),
    requested_at timestamptz,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    PRIMARY KEY (text_id, locale),
    CHECK (value IS NOT NULL OR status = 'pending'),
    CHECK (origin = 'machine' OR status <> 'pending')
);
-- +goose StatementEnd

-- +goose StatementBegin
-- Serves the machine translation sweeper looking for expired requests.
CREATE INDEX localized_text_translations_not_current ON localized_text_translations (status, requested_at)
    WHERE status <> 'current';
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_texts ENABLE ROW LEVEL SECURITY;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_texts FORCE ROW LEVEL SECURITY;
-- +goose StatementEnd

-- +goose StatementBegin
-- A tenant reads, writes and deletes only its own texts.
CREATE POLICY localized_texts_tenant ON localized_texts
    USING (tenant_id = current_setting('application.tenant', true))
    WITH CHECK (tenant_id = current_setting('application.tenant', true));
-- +goose StatementEnd

-- +goose StatementBegin
-- Every tenant reads global texts; SELECT policies do not apply to
-- INSERT, UPDATE or DELETE, so tenants can never change them.
CREATE POLICY localized_texts_global_read ON localized_texts FOR SELECT
    USING (tenant_id IS NULL);
-- +goose StatementEnd

-- +goose StatementBegin
-- Only the schema owner maintains global texts (from migrations).
CREATE POLICY localized_texts_global_maintenance ON localized_texts TO frappe_migration
    USING (tenant_id IS NULL)
    WITH CHECK (tenant_id IS NULL);
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations ENABLE ROW LEVEL SECURITY;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations FORCE ROW LEVEL SECURITY;
-- +goose StatementEnd

-- +goose StatementBegin
-- Translations follow their text: the subquery on localized_texts is
-- itself filtered by that table's policies, so a translation is visible
-- exactly when its text is, and writable only when the text belongs to
-- the current tenant (never for a global text).
CREATE POLICY localized_text_translations_tenant ON localized_text_translations
    USING (EXISTS (
        SELECT 1 FROM localized_texts
        WHERE localized_texts.id = text_id
          AND localized_texts.tenant_id = current_setting('application.tenant', true)
    ))
    WITH CHECK (EXISTS (
        SELECT 1 FROM localized_texts
        WHERE localized_texts.id = text_id
          AND localized_texts.tenant_id = current_setting('application.tenant', true)
    ));
-- +goose StatementEnd

-- +goose StatementBegin
CREATE POLICY localized_text_translations_global_read ON localized_text_translations FOR SELECT
    USING (EXISTS (
        SELECT 1 FROM localized_texts
        WHERE localized_texts.id = text_id AND localized_texts.tenant_id IS NULL
    ));
-- +goose StatementEnd

-- +goose StatementBegin
CREATE POLICY localized_text_translations_global_maintenance ON localized_text_translations TO frappe_migration
    USING (EXISTS (
        SELECT 1 FROM localized_texts
        WHERE localized_texts.id = text_id AND localized_texts.tenant_id IS NULL
    ))
    WITH CHECK (EXISTS (
        SELECT 1 FROM localized_texts
        WHERE localized_texts.id = text_id AND localized_texts.tenant_id IS NULL
    ));
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DROP TABLE localized_text_translations;
-- +goose StatementEnd

-- +goose StatementBegin
DROP TABLE localized_texts;
-- +goose StatementEnd

-- +goose StatementBegin
DROP TABLE locales;
-- +goose StatementEnd
