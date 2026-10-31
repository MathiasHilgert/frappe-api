-- Fail instead of queueing behind another session's lock; LOCAL ends with the migration's transaction.
SET LOCAL lock_timeout = '5s';

-- Trigram matching for text search. An extension is database-wide, so Platform installs it once for every module;
-- pg_trgm is trusted, so the database owner can install it without a superuser.
CREATE EXTENSION IF NOT EXISTS pg_trgm SCHEMA public;
