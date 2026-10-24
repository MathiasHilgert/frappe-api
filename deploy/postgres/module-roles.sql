-- Least-privilege plan: one role per module, with rights on its own schema (and the shared outbox) only.
-- Not applied by the dev stack yet; docs/database.md explains the rollout. Idempotent: safe to run again.
-- tests/Frappe.IntegrationTests/ModuleRoleTests.cs runs this script and proves the isolation it promises.
-- New module: copy the identity block and replace "identity".

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'frappe_identity') THEN
        CREATE ROLE frappe_identity NOLOGIN;
    END IF;
END
$$;

CREATE SCHEMA IF NOT EXISTS identity;
REVOKE ALL ON SCHEMA identity FROM PUBLIC;
GRANT USAGE ON SCHEMA identity TO frappe_identity;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity TO frappe_identity;
ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO frappe_identity;

-- Every module writes its cascading messages to Wolverine's outbox in its own transaction.
CREATE SCHEMA IF NOT EXISTS wolverine;
GRANT USAGE ON SCHEMA wolverine TO frappe_identity;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA wolverine TO frappe_identity;
ALTER DEFAULT PRIVILEGES IN SCHEMA wolverine GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO frappe_identity;
