-- Fail instead of queueing behind another session's lock; LOCAL ends with the migration's transaction.
SET LOCAL lock_timeout = '5s';

-- The migrations history table may have created the schema already.
CREATE SCHEMA IF NOT EXISTS geo;

-- Every geo table is platform-global reference data that only the migrator writes, so the app role reads each
-- table the owner creates here, and nothing else: no INSERT, UPDATE or DELETE, and no row-level security.
GRANT USAGE ON SCHEMA geo TO frappe_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA geo GRANT SELECT ON TABLES TO frappe_app;
