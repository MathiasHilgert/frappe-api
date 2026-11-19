# Frappé API

The backend of Frappé, the operating system for restaurants and cafés in Latin America.

Frappé is a modular monolith on .NET 11: one codebase and one deployable image, split into business modules that own
their data and talk to each other through integration events. Each module is a hexagon (domain, application,
infrastructure, HTTP adapter), and the cross-cutting work lives once in a shared platform layer.

## Architecture

The solution is `Frappe.slnx`. Shared platform libraries live under `src/Platform`, hosts under `src/Hosts` and business
modules under `src/Modules`, each module split into Domain, Application, Contracts, Infrastructure and Http projects. A
module depends on the platform and on other modules' Contracts only, never on another module's internals. Layering,
naming and documentation rules are enforced by `tests/Frappe.Architecture.Tests`, and the analyzers in `eng/` fail the
build on drift (warnings are errors, package versions are central and locked).

## Getting started

You need the .NET SDK pinned in `global.json`, [just](https://github.com/casey/just), Node.js, `jq` and Docker (only for
integration tests and the local stack). Every command is a `just` recipe: `just` lists them and `just --list <area>`
shows one area.

| Recipe | What it does |
|---|---|
| `just build` | Build the solution; warnings are errors |
| `just format` | Fix whitespace, style and analyzer findings |
| `just test all` | Run every suite (`just test architecture`, `just test platform`, `just test host` run one) |
| `just pr-check` | Run locally the checks a pull request most often fails: build, format, contracts, schema dumps |
| `just contracts update` | Regenerate the committed HTTP and event contracts |
| `just db migrate` / `just db schema` | Migrate the local database / regenerate the committed schema dumps |
| `just deps lock` / `just deps audit` | Rewrite the package lock files / list vulnerable packages |
| `just scaffold command Orders Fulfilment CancelOrder` | Generate the skeleton of a use case, error, event, endpoint or Bruno request |
| `just github conventions` | Test the pull request, commit and branch conventions |

A recipe whose subject does not exist yet prints a skip instead of failing.

This software is proprietary; see [LICENSE](LICENSE).
