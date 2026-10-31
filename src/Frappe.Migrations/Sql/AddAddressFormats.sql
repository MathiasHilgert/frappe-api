-- Fail instead of queueing behind another session's lock; LOCAL ends with the migration's transaction.
SET LOCAL lock_timeout = '5s';
