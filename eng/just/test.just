set working-directory := "../.."

# Run-level policies of `dotnet test` (Microsoft.Testing.Platform), before `--`: stop after 10 failed tests (exit 13),
# and give every test app its own TestResults/<project>/<target> directory. Each recipe adds its own `--timeout`
# (exit 3), which only counts while the test app runs; the outer `timeout` also covers the build.
policies := "--maximum-failed-tests 10 --results-directory-layout per-module"

# Every suite
all: platform

# Platform capabilities (no containers)
platform:
    timeout 240 dotnet test --project tests/Platform/Frappe.Platform.Tests --timeout 3m {{ policies }}
