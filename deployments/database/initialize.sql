-- Local development only: creates the two Postgres roles the application
-- expects (see internal/foundation/database/doc.go for the two-role
-- model). Mounted into /docker-entrypoint-initdb.d by compose.yaml, so the
-- official postgres image runs it once, automatically, the first time the
-- database volume is initialized.
--
-- The passwords below are development-only, checked into this repository
-- on purpose, and must never be reused anywhere real. Production
-- environments create these roles (with secret-manager-issued passwords)
-- through infrastructure automation, not this file.

-- Owns the schema and every object created in it; used only by
-- cmd/migrate through DATABASE_MIGRATION_URL, never by the running API.
CREATE ROLE frappe_migration WITH LOGIN PASSWORD 'frappe_migration_development_only' CREATEROLE;

-- Used by the running API (DATABASE_URL). No superuser, and RLS can never
-- be bypassed by this role, even on a table it happens to own.
CREATE ROLE frappe_application WITH LOGIN PASSWORD 'frappe_application_development_only' NOSUPERUSER NOBYPASSRLS;

GRANT ALL PRIVILEGES ON DATABASE frappe TO frappe_migration;
