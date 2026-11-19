#!/usr/bin/env bash
# Writes the committed schema of every module schema: `pg_dump --schema-only` of its schema, normalized, into <out root> at the
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

# schema -> where its dump is committed
dumps=(
  # "<schema> <directory of its schema.sql>", one line per module, for example "orders src/Modules/Orders/Frappe.Orders.Infrastructure/Persistence"
)

# Every schema the Migrator created must be in the list above, or a new module would never be checked.
listed=$(printf '%s\n' "${dumps[@]}" | cut -d' ' -f1 | sort)
present=$(COMPOSE_PROJECT_NAME=$project docker compose -f deploy/compose.yaml exec -T postgres \
  sh -c 'psql --username="$POSTGRES_USER" --dbname=frappe --tuples-only --no-align --command="SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE '"'"'pg\_%'"'"' AND nspname NOT IN ('"'"'public'"'"', '"'"'information_schema'"'"', '"'"'topology'"'"', '"'"'tiger'"'"', '"'"'tiger_data'"'"') ORDER BY 1"' | sort)
unlisted=$(comm -13 <(echo "$listed") <(echo "$present"))
if [ -n "$unlisted" ]; then
  echo "::error::Schemas with no committed dump: $unlisted. Add them to the dumps list in .github/scripts/schema-dump.sh." >&2
  exit 1
fi

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
