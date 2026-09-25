-- Local development only: creates the two Postgres roles the application
-- expects (see internal/foundation/database/doc.go for the two-role
-- model). Mounted into /docker-entrypoint-initdb.d by compose.yaml, so the
-- official postgres image runs it once, automatically, as the postgres
-- superuser connected to the frappe database (POSTGRES_DB), the first time
-- the database volume is initialized.
--
-- The passwords below are development-only, checked into this repository
-- on purpose, and must never be reused anywhere real. Production
-- environments create these roles (with secret-manager-issued passwords)
-- through infrastructure automation, not this file, and must apply the
-- same ownership statements below.

-- Owns the database, the public schema and every object created in it;
-- used only by cmd/migrate through DATABASE_MIGRATION_URL, never by the
-- running API. No CREATEROLE: migrations only grant privileges to the
-- existing frappe_application role, they never create or alter roles, so
-- the migration role does not need (and must not get) that privilege.
CREATE ROLE frappe_migration WITH LOGIN PASSWORD 'frappe_migration_development_only' NOSUPERUSER NOCREATEROLE NOCREATEDB;

-- Used by the running API (DATABASE_URL). No superuser, and RLS can never
-- be bypassed by this role, even on a table it happens to own.
CREATE ROLE frappe_application WITH LOGIN PASSWORD 'frappe_application_development_only' NOSUPERUSER NOBYPASSRLS;

-- Since Postgres 15, the public schema is owned by pg_database_owner (the
-- database owner, postgres here) and ordinary roles may no longer CREATE
-- in it. Making frappe_migration the owner of the database, and therefore
-- of public through pg_database_owner, would work too, but only while
-- public keeps that default owner; owning public explicitly is what lets
-- migrations create tables in it and run GRANT USAGE ON SCHEMA public TO
-- frappe_application (see migrations/20260924000000_application_role_privileges.sql).
ALTER DATABASE frappe OWNER TO frappe_migration;
ALTER SCHEMA public OWNER TO frappe_migration;
