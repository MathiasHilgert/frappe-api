#!/usr/bin/env bash
# Creates the database roles. The postgres image runs it once, as the superuser, on an empty data
# directory; a database created before this script existed needs a fresh volume.
#   frappe_owner owns the database and runs the migrations.
#   frappe_app is the runtime role: no row-level security bypass, no DDL, owns nothing, and gets
#   access to each table only through the migration that creates it.
set -euo pipefail

: "${FRAPPE_OWNER_PASSWORD:?FRAPPE_OWNER_PASSWORD must be set}"
: "${FRAPPE_APP_PASSWORD:?FRAPPE_APP_PASSWORD must be set}"

psql --variable ON_ERROR_STOP=1 --no-psqlrc \
  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set database="$POSTGRES_DB" \
  --set owner_password="$FRAPPE_OWNER_PASSWORD" \
  --set app_password="$FRAPPE_APP_PASSWORD" <<'SQL'
CREATE ROLE frappe_owner LOGIN BYPASSRLS PASSWORD :'owner_password';
CREATE ROLE frappe_app LOGIN NOBYPASSRLS PASSWORD :'app_password';

ALTER DATABASE :"database" OWNER TO frappe_owner;
REVOKE ALL ON DATABASE :"database" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"database" TO frappe_owner, frappe_app;
-- The maintenance database stays reachable only by the superuser.
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;

-- statement_timeout stays below the outbound and request timeouts so the database fails first.
ALTER ROLE frappe_app SET statement_timeout = '15s';
ALTER ROLE frappe_app SET lock_timeout = '5s';
ALTER ROLE frappe_app SET idle_in_transaction_session_timeout = '60s';
SQL
