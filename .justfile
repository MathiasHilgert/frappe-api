# Frappé developer commands: `just <area> <action>`. `just` lists them; `just --list <area>` shows one area.
# Secrets come from deploy/.env (cp deploy/.env.example deploy/.env first), shared with docker compose.
set dotenv-path := "deploy/.env"
set shell := ["bash", "-euo", "pipefail", "-c"]

# The whole stack in containers: just stack up | down | reset | ps | health | logs <service>
mod stack "eng/just/stack.just"
# Only the infrastructure and LGTM, to run the hosts from here or the IDE: just infra up
mod infra "eng/just/infra.just"
# Database: just db migrate
mod db "eng/just/db.just"
# Frappé hosts on this machine against the compose infrastructure: just run api | worker
mod run "eng/just/run.just"
# Test suites, each under a timeout: just test all | platform | architecture | e2e
mod test "eng/just/test.just"
# The image: just docker image
mod docker "eng/just/docker.just"
# Packages: just deps lock | audit
mod deps "eng/just/deps.just"
# GitHub: just github conventions (the tests of the CI's conventions and change detection)
mod github "eng/just/github.just"

# List the commands
default:
    @just --list --list-submodules

# Build the solution (warnings are errors)
build:
    dotnet build Frappe.slnx
