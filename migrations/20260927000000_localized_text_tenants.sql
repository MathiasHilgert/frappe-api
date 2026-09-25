-- +goose Up
-- +goose StatementBegin
-- The periodic localized text sweeps (expired machine translation
-- requests, orphan texts; see internal/foundation/i18n/machinetranslation)
-- run once per tenant, in a transaction scoped by Row Level Security.
-- There is no tenant model yet, so they list tenants from the texts
-- themselves. localized_text_tenants() is the only way across Row Level
-- Security: a SECURITY DEFINER function owned by frappe_migration that
-- returns tenant identifiers and nothing else. Its owner needs a policy to
-- see tenant rows (FORCE ROW LEVEL SECURITY applies to it too); that
-- policy is SELECT-only, and frappe_migration owns the schema anyway.
CREATE POLICY localized_texts_tenant_directory ON localized_texts FOR SELECT TO frappe_migration
    USING (tenant_id IS NOT NULL);
-- +goose StatementEnd

-- +goose StatementBegin
CREATE FUNCTION localized_text_tenants() RETURNS SETOF text
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT DISTINCT tenant_id FROM localized_texts WHERE tenant_id IS NOT NULL ORDER BY tenant_id
$$;
-- +goose StatementEnd

-- +goose StatementBegin
REVOKE ALL ON FUNCTION localized_text_tenants() FROM PUBLIC;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT EXECUTE ON FUNCTION localized_text_tenants() TO frappe_application;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DROP FUNCTION localized_text_tenants();
-- +goose StatementEnd

-- +goose StatementBegin
DROP POLICY localized_texts_tenant_directory ON localized_texts;
-- +goose StatementEnd
