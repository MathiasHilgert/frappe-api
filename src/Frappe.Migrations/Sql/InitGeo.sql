-- Fail instead of queueing behind another session's lock; LOCAL ends with the migration's transaction.
SET LOCAL lock_timeout = '5s';

-- The migrations history table may have created the schema already.
CREATE SCHEMA IF NOT EXISTS geo;

-- Every geo table is platform-global reference data that only the migrator writes, so the app role may only read
-- it: each table's own migration grants SELECT on it, and nothing else; no row-level security.
GRANT USAGE ON SCHEMA geo TO frappe_app;
