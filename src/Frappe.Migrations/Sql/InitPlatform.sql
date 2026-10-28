-- Fail instead of queueing behind another session's lock; LOCAL ends with the migration's transaction.
SET LOCAL lock_timeout = '5s';

-- The migrations history table may have created the schema already.
CREATE SCHEMA IF NOT EXISTS platform;

-- The app role reads and writes only what each migration grants it, table by table.
GRANT USAGE ON SCHEMA platform TO frappe_app;

-- Explicit even though PostgreSQL 15 and later ship it this way: only migrations create objects.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
