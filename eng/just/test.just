set working-directory := "../.."

# Faster local integration runs: `export FRAPPE_TEST_REUSE=1` keeps the Postgres and Valkey test containers between runs
# (restarted instead of created; about half the startup). It is opt-in and ignored when CI=true. A reused Postgres is
# emptied of leftover databases at the start of each run, so run only one test session per test app per worktree at a
# time (two at once share one container and clobber each other; unset the variable for parallel runs). Ryuk needs no
# change (it simply does not own a reused container); remove them with `just test reuse-clean`, and after a change to a
# fixture's image or settings.

# Run-level policies of `dotnet test` (Microsoft.Testing.Platform), before `--`: stop after 10 failed tests (exit 13),
# and give every test app its own TestResults/<project>/<target> directory. Each recipe adds its own `--timeout`
# (exit 3), which only counts while the test app runs; the outer `timeout` also covers the build.
policies := "--maximum-failed-tests 10 --results-directory-layout per-module"

# Every suite
all: platform architecture host

# Platform capabilities: unit (no containers), then integration (Docker)
platform: platform-unit platform-integration

# The platform libraries in process (no Docker; seconds)
platform-unit: (suite "tests/Platform/Frappe.Platform.UnitTests" "240" "3m")

# The platform libraries against real Postgres and Valkey (Testcontainers; needs Docker)
platform-integration: (suite "tests/Platform/Frappe.Platform.IntegrationTests" "240" "3m")

# Layering and convention rules on the sources and the compiled assemblies
architecture: (suite "tests/Frappe.Architecture.Tests" "120" "1m")

# The Api host composed with every module: the committed HTTP contract (no Docker; seconds)
host: (suite "tests/Hosts/Frappe.Api.IntegrationTests" "180" "1m")

# Remove the containers FRAPPE_TEST_REUSE=1 kept (all worktrees)
reuse-clean:
    docker ps -aq --filter label=frappe.tests.reuse | xargs -r docker rm -f

# One test app under its timeouts; a suite whose project does not exist yet is skipped, not failed
[private]
suite project seconds timeout:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ ! -d "{{ project }}" ]; then
        echo "skip: {{ project }} does not exist yet"
        exit 0
    fi
    timeout {{ seconds }} dotnet test --project {{ project }} --timeout {{ timeout }} {{ policies }}
