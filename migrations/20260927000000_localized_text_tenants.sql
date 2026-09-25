-- +goose Up
-- +goose StatementBegin
-- The periodic localized text sweeps (expired machine translation
-- requests, orphan texts; see internal/foundation/i18n/machinetranslation)
-- run once per tenant, in a transaction scoped by Row Level Security.
-- There is no tenant model yet, so tenants are recorded here, by a
-- trigger, as texts are created. The table holds tenant identifiers only
-- and has no Row Level Security; the application role cannot read it
-- directly, only through localized_text_tenants() below. Tenant
-- identifiers are therefore listable by the application role, nothing
-- else about other tenants is. Entries are never removed: a tenant
-- without texts left only costs the sweeps an empty transaction.
CREATE TABLE localized_text_tenant_directory (
    tenant_id text PRIMARY KEY CHECK (tenant_id <> '')
);
-- +goose StatementEnd

-- +goose StatementBegin
REVOKE ALL ON localized_text_tenant_directory FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
-- Backfill texts created before this migration. The owner is subject to
-- FORCE ROW LEVEL SECURITY and would see global texts only, so it is
-- lifted for this one statement, inside the migration's transaction.
ALTER TABLE localized_texts NO FORCE ROW LEVEL SECURITY;
-- +goose StatementEnd

-- +goose StatementBegin
INSERT INTO localized_text_tenant_directory (tenant_id)
SELECT DISTINCT tenant_id FROM localized_texts WHERE tenant_id IS NOT NULL
ON CONFLICT DO NOTHING;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_texts FORCE ROW LEVEL SECURITY;
-- +goose StatementEnd

-- +goose StatementBegin
-- Runs as the table owner, so the application role, which cannot write
-- the directory, still records its own tenant. It writes nothing else.
CREATE FUNCTION localized_text_record_tenant() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    INSERT INTO localized_text_tenant_directory (tenant_id) VALUES (NEW.tenant_id) ON CONFLICT DO NOTHING;
    RETURN NULL;
END
$$;
-- +goose StatementEnd

-- +goose StatementBegin
REVOKE ALL ON FUNCTION localized_text_record_tenant() FROM PUBLIC;
-- +goose StatementEnd

-- +goose StatementBegin
CREATE TRIGGER localized_texts_record_tenant AFTER INSERT ON localized_texts
    FOR EACH ROW WHEN (NEW.tenant_id IS NOT NULL)
    EXECUTE FUNCTION localized_text_record_tenant();
-- +goose StatementEnd

-- +goose StatementBegin
-- The only read access to the directory: tenant identifiers, sorted.
CREATE FUNCTION localized_text_tenants() RETURNS SETOF text
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT tenant_id FROM localized_text_tenant_directory ORDER BY tenant_id
$$;
-- +goose StatementEnd

-- +goose StatementBegin
REVOKE ALL ON FUNCTION localized_text_tenants() FROM PUBLIC;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT EXECUTE ON FUNCTION localized_text_tenants() TO frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
-- failed: a machine translation given up (rejected by the provider, or
-- requested too many times); it keeps no value, or the previous machine
-- value, and is requested again only when the source changes.
ALTER TABLE localized_text_translations DROP CONSTRAINT localized_text_translations_status_check;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations ADD CONSTRAINT localized_text_translations_status_check
    CHECK (status IN ('current', 'stale', 'pending', 'failed'));
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations DROP CONSTRAINT localized_text_translations_check;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations ADD CONSTRAINT localized_text_translations_check
    CHECK (value IS NOT NULL OR status IN ('pending', 'failed'));
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations DROP CONSTRAINT localized_text_translations_check1;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations ADD CONSTRAINT localized_text_translations_check1
    CHECK (origin = 'machine' OR status NOT IN ('pending', 'failed'));
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DELETE FROM localized_text_translations WHERE status = 'failed' AND value IS NULL;
-- +goose StatementEnd

-- +goose StatementBegin
UPDATE localized_text_translations SET status = 'stale' WHERE status = 'failed';
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations DROP CONSTRAINT localized_text_translations_check1;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations ADD CONSTRAINT localized_text_translations_check1
    CHECK (origin = 'machine' OR status <> 'pending');
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations DROP CONSTRAINT localized_text_translations_check;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations ADD CONSTRAINT localized_text_translations_check
    CHECK (value IS NOT NULL OR status = 'pending');
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations DROP CONSTRAINT localized_text_translations_status_check;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER TABLE localized_text_translations ADD CONSTRAINT localized_text_translations_status_check
    CHECK (status IN ('current', 'stale', 'pending'));
-- +goose StatementEnd

-- +goose StatementBegin
DROP FUNCTION localized_text_tenants();
-- +goose StatementEnd

-- +goose StatementBegin
DROP TRIGGER localized_texts_record_tenant ON localized_texts;
-- +goose StatementEnd

-- +goose StatementBegin
DROP FUNCTION localized_text_record_tenant();
-- +goose StatementEnd

-- +goose StatementBegin
DROP TABLE localized_text_tenant_directory;
-- +goose StatementEnd
