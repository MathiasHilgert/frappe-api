# Frappé developer commands: `just <area> <action>`. `just` lists them; `just --list <area>` shows one area.
set shell := ["bash", "-euo", "pipefail", "-c"]

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
