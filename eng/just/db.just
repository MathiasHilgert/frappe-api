set working-directory := "../.."

# Run the Migrator (roles, migrations, Quartz tables, grants) against the compose database
migrate:
    docker compose -f deploy/compose.yaml run --rm --build migrator
