set working-directory := "../.."

# Run the Migrator (roles, migrations, Quartz tables, grants) against the compose database
migrate:
    docker compose -f deploy/compose.yaml run --rm --build migrator

# Regenerate every module's committed schema.sql from a throwaway database migrated from empty (CI fails on any difference)
schema:
    #!/usr/bin/env bash
    set -euo pipefail
    # One project and one image tag per checkout: worktrees regenerating their dumps at the same time never share a
    # database or overwrite each other's Migrator image.
    id=$(pwd | sha1sum | cut -c1-8)
    export COMPOSE_PROJECT_NAME="frappe-schema-$id" FRAPPE_IMAGE_TAG="schema-$id"
    # Ephemeral host ports: the throwaway stack never collides with the one `just stack up` runs.
    export POSTGRES_PORT=0
    trap 'docker compose -f deploy/compose.yaml down --volumes' EXIT
    docker compose -f deploy/compose.yaml run --rm --build migrator
    .github/scripts/schema-dump.sh "$COMPOSE_PROJECT_NAME" .
