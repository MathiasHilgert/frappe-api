set working-directory := "../.."

# Run-level policies of `dotnet test` (Microsoft.Testing.Platform), before `--`: stop after 10 failed tests (exit 13),
# and give every test app its own TestResults/<project>/<target> directory. Each recipe adds its own `--timeout`
# (exit 3), which only counts while the test app runs; the outer `timeout` also covers the build.
policies := "--maximum-failed-tests 10 --results-directory-layout per-module"

# Every suite
all: platform architecture geo

# Platform capabilities (no containers)
platform:
    timeout 240 dotnet test --project tests/Platform/Frappe.Platform.Tests --timeout 3m {{ policies }}

# Layering rules on the compiled assemblies
architecture:
    timeout 120 dotnet test --project tests/Frappe.Architecture.Tests --timeout 1m {{ policies }}

# Both Geo suites: unit, then integration
geo: geo-unit geo-integration

# Geo's domain, use cases and adapters in process (no Docker; seconds)
geo-unit:
    timeout 180 dotnet test --project tests/Modules/Geo/Frappe.Geo.UnitTests --timeout 1m {{ policies }}

# Geo against real Postgres and Valkey (Testcontainers; needs Docker)
geo-integration:
    timeout 400 dotnet test --project tests/Modules/Geo/Frappe.Geo.IntegrationTests --timeout 5m {{ policies }}

# The real image end to end: builds it, then runs Migrator, Api and Worker (needs Docker; not part of `all`)
e2e:
    timeout 900 dotnet test --project tests/Frappe.EndToEnd.Tests --timeout 12m {{ policies }}
