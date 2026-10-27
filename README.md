# Frappé API

The backend of Frappé, the operating system for restaurants: one multi-tenant platform for the dining room, counter, kitchen, cash, payments, fiscal invoicing, inventory and the digital menu.

## Prerequisites

- [.NET SDK 10.0.100](https://dotnet.microsoft.com/download/dotnet/10.0) or a later 10.0.1xx patch (pinned in `global.json`).

## Build and test

The `justfile` holds every command CI runs. [Just](https://just.systems) is optional: each recipe is plain `dotnet` commands you can run by hand.

```bash
just              # list the recipes
just build        # restore exactly as locked, then build
just test         # all tests; or: just test unit | integration | architecture
just format       # format with CSharpier (format-check only verifies)
just lint         # rebuild so every analyzer runs
just audit        # fail on a high or critical package advisory
just check        # everything CI runs, in the same order
```

The build treats warnings as errors, so a clean build means every analyzer rule passes.

## Run

```bash
just run                              # or: just run api | worker | scheduler
dotnet run --project src/Frappe.Host  # the same without Just
```

One binary runs every process role. Select it with `Platform:Role`, for example `--Platform:Role worker` or the environment variable `Platform__Role=worker`:

| Role | Purpose |
|---|---|
| `all` (default) | Everything in one process |
| `api` | HTTP API |
| `worker` | Background message processing |
| `scheduler` | Scheduled jobs |

Every role serves the health endpoints: `/health/live` (the process answers) and `/health/ready` (critical dependencies are healthy).

## Repository layout

| Path | Contents |
|---|---|
| `src/Frappe.Kernel` | Types shared by every module |
| `src/Frappe.Platform` | Adapters, middleware, health and hosting defaults |
| `src/Frappe.Host` | Composition root |
| `tests/` | Architecture and host tests |
| `eng/` | Analyzer configuration, banned APIs, code metrics and CI scripts |
| `.github/` | CI workflows, Dependabot, labels and the pull request template |

## License

Proprietary. See [LICENSE](LICENSE).
