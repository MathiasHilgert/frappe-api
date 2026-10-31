-- Fail instead of queueing behind another session's lock; LOCAL ends with the migration's transaction.
SET LOCAL lock_timeout = '5s';

-- The migrations history table may have created the schema already.
CREATE SCHEMA IF NOT EXISTS geo;

-- The app role reads only the tables each migration grants it, one by one.
GRANT USAGE ON SCHEMA geo TO frappe_app;
