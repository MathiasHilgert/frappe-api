-- Cluster-safe scheduled tasks: the db-scheduler 16.12.0 table, copied from the official Postgres DDL
-- (db-scheduler/src/test/resources/postgresql_tables.sql at tag v16.12.0) and qualified with the platform schema.
-- Keep the columns identical to the official DDL; the library's SQL depends on them. The library never creates tables:
-- db-scheduler.table-name points at this one.
--
-- One row per task instance (primary key task_name, task_instance): scheduling an existing natural key is a no-op or
-- an explicit reschedule, never a second execution. An instance picks a due row by an optimistic update of version;
-- picked, picked_by and last_heartbeat let the other instances take over an execution whose instance died.
create table platform.scheduled_tasks (
    task_name text not null,
    task_instance text not null,
    task_data bytea,
    execution_time timestamp with time zone not null,
    picked boolean not null,
    picked_by text,
    last_success timestamp with time zone,
    last_failure timestamp with time zone,
    consecutive_failures int,
    last_heartbeat timestamp with time zone,
    version bigint not null,
    priority smallint,
    primary key (task_name, task_instance)
);
-- Due executions are polled by execution_time, dead ones found by last_heartbeat. The official priority index is left
-- out: priority ordering is not enabled (db-scheduler.priority-enabled), so the index would only cost writes.
create index scheduled_tasks_execution_time_idx on platform.scheduled_tasks (execution_time);
create index scheduled_tasks_last_heartbeat_idx on platform.scheduled_tasks (last_heartbeat);
