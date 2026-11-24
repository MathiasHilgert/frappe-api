#!/usr/bin/env bash
# Writes the committed schema of every schema the Migrator created: `pg_dump --schema-only` of its schema, normalized, into <out root> at the
# module's own path. Usage: schema-dump.sh <compose project> <out root>
#
# Run from the repository root, against a compose project whose postgres service the Migrator already migrated. It dumps
# with the pg_dump of the same postgis 18 container that serves the database, so client and server versions always match.
# Extensions (postgis) live outside the module schemas and are not dumped, so their version never shows up as drift.
# Grants and default privileges are kept: a changed grant is drift too. Outbox, inbox and __EFMigrationsHistory live in each
# schema, so they are part of the dump.
set -euo pipefail

project=$1
out_root=$2

# Where each schema's dump is committed: a module's schema (its lower-case name) next to its Infrastructure persistence, and
# the platform capabilities that own a schema (Scheduling's quartz) in their own project.
dump_directory() {
  case "$1" in
    quartz) echo "src/Platform/Frappe.Scheduling" ;;
    *) find src/Modules -mindepth 3 -maxdepth 3 -type d -name Persistence -path "*/Frappe.*.Infrastructure/Persistence" 2> /dev/null \
         | while read -r dir; do
             module=$(basename "$(dirname "$dir")" | sed -E 's/^Frappe\.(.*)\.Infrastructure$/\1/' | tr '[:upper:]' '[:lower:]')
             if [ "$module" = "$1" ]; then echo "$dir"; fi
           done ;;
  esac
}

present=$(COMPOSE_PROJECT_NAME=$project docker compose -f deploy/compose.yaml exec -T postgres \
  sh -c 'psql --username="$POSTGRES_USER" --dbname=frappe --tuples-only --no-align --command="SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE '"'"'pg\_%'"'"' AND nspname NOT IN ('"'"'public'"'"', '"'"'information_schema'"'"', '"'"'topology'"'"', '"'"'tiger'"'"', '"'"'tiger_data'"'"') ORDER BY 1"' | sort)

dumps=()
for schema in $present; do
  directory=$(dump_directory "$schema")
  if [ -z "$directory" ]; then
    echo "::error::Schema '$schema' has no place for its committed dump: a module's lives in src/Modules/<Module>/Frappe.<Module>.Infrastructure/Persistence." >&2
    exit 1
  fi
  dumps+=("$schema $directory")
done

for entry in "${dumps[@]}"; do
  read -r schema directory <<< "$entry"
  mkdir -p "$out_root/$directory"
  # --restrict-key makes the \restrict line deterministic. Normalization drops what changes between runs or servers: the
  # SET and set_config preamble and every comment, anchored at column 0 so indented SQL is never touched; cat -s then
  # collapses the blank lines left behind.
  COMPOSE_PROJECT_NAME=$project docker compose -f deploy/compose.yaml exec -T postgres \
    sh -c 'pg_dump --schema-only --no-owner --restrict-key=frappe --schema="$1" --username="$POSTGRES_USER" --dbname=frappe' _ "$schema" \
    | grep -Ev '^(SET |SELECT pg_catalog\.set_config|--)' \
    | cat -s > "$out_root/$directory/schema.sql"
  if ! grep -q '^CREATE TABLE ' "$out_root/$directory/schema.sql"; then
    echo "::error::The dump of schema '$schema' has no tables: was the database migrated?" >&2
    exit 1
  fi
done
