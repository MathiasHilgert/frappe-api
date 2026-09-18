#!/bin/sh
# Creates Frappé's least-privilege roles. Idempotent: runs once from
# /docker-entrypoint-initdb.d on a new volume (compose and Testcontainers share this file)
# and can be re-run on an existing one:
#   docker compose exec postgres /docker-entrypoint-initdb.d/01-frappe-roles.sh
#   frappe_owner: runs Flyway, owns module schemas and tables.
#   frappe_app:   runtime role; USAGE on owner schemas and DML on owner tables, no DDL.
# Existing roles keep their passwords.
set -eu

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v owner_password="${FRAPPE_OWNER_PASSWORD:-frappe_owner}" \
  -v app_password="${FRAPPE_APP_PASSWORD:-frappe_app}" <<'SQL'
select format('create role frappe_owner login password %L', :'owner_password')
where not exists (select from pg_roles where rolname = 'frappe_owner') \gexec
select format('create role frappe_app login password %L', :'app_password')
where not exists (select from pg_roles where rolname = 'frappe_app') \gexec

revoke temporary on database :"db" from public;
revoke all on schema public from public;
grant connect, create on database :"db" to frappe_owner;
grant connect on database :"db" to frappe_app;

alter default privileges for role frappe_owner grant usage on schemas to frappe_app;
alter default privileges for role frappe_owner
  grant select, insert, update, delete on tables to frappe_app;
alter default privileges for role frappe_owner grant usage, select on sequences to frappe_app;
SQL
