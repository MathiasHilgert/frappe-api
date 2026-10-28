# Frappé API

The backend of Frappé, the operating system for restaurants: one multi-tenant platform for the dining room, counter, kitchen, cash, payments, fiscal invoicing, inventory and the digital menu.

## Prerequisites

- [.NET SDK 10.0.100](https://dotnet.microsoft.com/download/dotnet/10.0) or a later 10.0.1xx patch (pinned in `global.json`).
- [Docker](https://docs.docker.com/get-docker/) with Compose v2, only for the container image and the local environment.

## Build and test

The `justfile` holds every command CI runs. [Just](https://just.systems) is optional: each recipe is plain `dotnet` or `docker` commands you can run by hand.

```bash
just              # list the recipes
just build        # restore exactly as locked, then build
just package      # the container image frappe-api:local
just test         # all tests; or: just test unit | integration | architecture
just format       # format with CSharpier (format-check only verifies)
just lint         # rebuild so every analyzer runs
just audit        # fail on a high or critical package advisory
just check        # everything CI runs, in the same order
```

The build treats warnings as errors, so a clean build means every analyzer rule passes.

## Run

`just run` starts the host as a local process; `just start` runs it in containers with PostgreSQL (see Local environment).

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

## Local environment

`compose.yaml` runs PostgreSQL and the API image in containers, for local development only.

```bash
just start         # build the image, start both containers and wait until they are up
just status        # each container and its health
just logs          # follow every container; or: just logs postgres | api
just stop          # stop and remove the containers; the database volume is kept
```

The API listens on <http://localhost:8080> (`/health/live`, `/health/ready`) and PostgreSQL on `localhost:5432`, both bound to the loopback interface only. The credentials in `compose.yaml` are throwaway local defaults; override `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_PORT` or `API_PORT` in an untracked `.env` file. To delete the database as well, run `docker compose down --volumes`.

The image runs the host on the chiseled ASP.NET runtime as a non-root user, listening on port 8080; select the role with `Platform__Role`, for example `docker run -e Platform__Role=worker frappe-api:local`.

## Repository layout

| Path | Contents |
|---|---|
| `src/Frappe.Kernel` | Types shared by every module |
| `src/Frappe.Platform` | Adapters, middleware, health and hosting defaults |
| `src/Frappe.Host` | Composition root |
| `tests/` | Architecture and host tests |
| `eng/` | Analyzer configuration, banned APIs, code metrics and CI scripts |
| `.github/` | CI workflows, Dependabot, labels and the pull request template |
| `Dockerfile`, `compose.yaml` | Container image and local environment |

## License

Proprietary. See [LICENSE](LICENSE).
