#!/bin/sh
# Creates Frappé's least-privilege roles on a fresh database.
# Runs once from /docker-entrypoint-initdb.d (compose and Testcontainers share this file).
#   frappe_owner: runs Flyway, owns module schemas and tables.
#   frappe_app:   runtime role; USAGE on owner schemas and DML on owner tables, no DDL.
set -eu

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v owner_password="${FRAPPE_OWNER_PASSWORD:-frappe_owner}" \
  -v app_password="${FRAPPE_APP_PASSWORD:-frappe_app}" <<'SQL'
create role frappe_owner login password :'owner_password';
create role frappe_app login password :'app_password';

grant connect, create on database :"db" to frappe_owner;
grant connect on database :"db" to frappe_app;
revoke create on schema public from public;

alter default privileges for role frappe_owner grant usage on schemas to frappe_app;
alter default privileges for role frappe_owner
  grant select, insert, update, delete on tables to frappe_app;
alter default privileges for role frappe_owner grant usage, select on sequences to frappe_app;
SQL
