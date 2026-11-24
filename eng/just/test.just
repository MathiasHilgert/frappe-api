set working-directory := "../.."

# Faster local integration runs: `export FRAPPE_TEST_REUSE=1` keeps the Postgres, Valkey and NATS test containers between
# runs (restarted instead of created; about half the startup; Postgres data lives on tmpfs, so a restart empties it and the template databases are rebuilt). It is opt-in and ignored when CI=true. The end-to-end suite
# always starts fresh. A reused Postgres is emptied of leftover databases at the start of each run, so run only one test
# session per test app per worktree at a time (a reused container is keyed by worktree, test app and fixture, so different
# test apps in parallel are fine; two sessions of the same app share one container and clobber each other, which is why
# the gate's coverage step runs with reuse off). Ryuk needs no change (it simply does not own a reused container);
# remove them with `just test reuse-clean`, and after a change to a fixture's image or settings.

# Run-level policies of `dotnet test` (Microsoft.Testing.Platform), before `--`: stop after 10 failed tests (exit 13),
# and give every test app its own TestResults/<project>/<target> directory. Each recipe adds its own `--timeout`
# (exit 3), which only counts while the test app runs; the outer `timeout` also covers the build.
policies := "--maximum-failed-tests 10 --results-directory-layout per-module"

# Every suite
all: platform architecture host migrator

# Platform capabilities: unit (no containers), then integration (Docker)
platform: platform-unit platform-integration

platform-unit:
    timeout 240 dotnet test --project tests/Platform/Frappe.Platform.UnitTests --timeout 3m {{ policies }}

platform-integration:
    timeout 240 dotnet test --project tests/Platform/Frappe.Platform.IntegrationTests --timeout 3m {{ policies }}

# Layering rules on the compiled assemblies
architecture:
    timeout 120 dotnet test --project tests/Frappe.Architecture.Tests --timeout 1m {{ policies }}

# The Api host composed with its modules: the committed HTTP contract (no Docker; seconds)
host:
    timeout 180 dotnet test --project tests/Hosts/Frappe.Api.IntegrationTests --timeout 1m {{ policies }}

# The Migrator against a real Postgres: the platform's databases and the listed modules' (needs Docker)
migrator:
    timeout 180 dotnet test --project tests/Hosts/Frappe.Migrator.IntegrationTests --timeout 2m {{ policies }}

# The real image end to end: builds it, then runs Migrator, Api and Worker (needs Docker; not part of `all`)
e2e:
    timeout 900 dotnet test --project tests/Frappe.EndToEnd.Tests --timeout 12m {{ policies }}

# Remove the containers FRAPPE_TEST_REUSE=1 kept (all worktrees)
reuse-clean:
    docker ps -aq --filter label=frappe.tests.reuse | xargs -r docker rm -f
