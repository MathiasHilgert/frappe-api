# Frappé developer commands: `just <area> <action>`. `just` lists them; `just --list <area>` shows one area.
set shell := ["bash", "-euo", "pipefail", "-c"]

# Packages: just deps lock | audit
mod deps "eng/just/deps.just"

# List the commands
default:
    @just --list --list-submodules

# Build the solution (warnings are errors)
build:
    dotnet build Frappe.slnx
