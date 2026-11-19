# Frappé developer commands: `just <area> <action>`. `just` lists them; `just --list <area>` shows one area.
# Secrets come from deploy/.env when it exists (cp deploy/.env.example deploy/.env), shared with docker compose.
set dotenv-path := "deploy/.env"
set dotenv-required := false
set shell := ["bash", "-euo", "pipefail", "-c"]

# Test suites, each under a timeout: just test all | platform | architecture | host
mod test "eng/just/test.just"
# Database: just db migrate | schema
mod db "eng/just/db.just"
# The committed HTTP and event contracts: just contracts update
mod contracts "eng/just/contracts.just"
# Packages: just deps lock | audit
mod deps "eng/just/deps.just"
# Skeletons for writers (use case, error, event, endpoint, Bruno request): just scaffold command Orders Fulfilment CancelOrder
mod scaffold "eng/just/scaffold.just"
# GitHub: just github conventions | protect | labels
mod github "eng/just/github.just"

# List the commands
default:
    @just --list --list-submodules

# Build the solution (warnings are errors)
build:
    dotnet build Frappe.slnx

# Fix whitespace, style and analyzer findings of the whole solution
format:
    dotnet format Frappe.slnx

# Before a push: the CI checks a branch most often fails on (conventions, Bruno seq, build, format, model, contracts, schema dumps)
pr-check base="origin/main":
    eng/pr-check.sh {{ base }}
