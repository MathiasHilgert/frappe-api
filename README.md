# Frappé API

The backend of Frappé, the operating system for restaurants: one multi-tenant platform for the dining room, counter, kitchen, cash, payments, fiscal invoicing, inventory and the digital menu.

## Prerequisites

- [.NET SDK 10.0.100](https://dotnet.microsoft.com/download/dotnet/10.0) or any later 10.0 SDK (`global.json` rolls forward to the newest installed 10.0 feature band).
- [Docker](https://docs.docker.com/get-docker/) with Compose v2, for the local environment (PostgreSQL 18), the container image and the integration tests, which start their own PostgreSQL 18 container.

## Build and test

The `justfile` holds every command CI runs. [Just](https://just.systems) is optional: each recipe is plain `dotnet` or `docker` commands you can run by hand.

```bash
just              # list the recipes
just build        # restore exactly as locked, then build
just package      # the container images frappe-api:local and frappe-migrations:local
just test         # all tests; or: just test unit | integration | architecture
just format       # format with CSharpier (format-check only verifies)
just lint         # rebuild so every analyzer runs
just migrations-check  # fail when the model has changes no migration captures
just audit        # fail on a high or critical package advisory
just check        # everything CI runs, in the same order
```

The build treats warnings as errors, so a clean build means every analyzer rule passes.

## Run

`just run` starts the host as a local process; `just start` runs it in containers with PostgreSQL (see Local environment).

The host needs the connection string `ConnectionStrings:Frappe` and refuses to start without it. It is a secret, so keep it out of `appsettings*.json`: `just start` passes it to the container, and for `just run` store it in user-secrets, pointing at the PostgreSQL that `just start` exposes on `localhost:5432`, as the app role `frappe_app` (elsewhere use the environment variable `ConnectionStrings__Frappe`). `just run` runs the host in the `Development` environment on <http://localhost:5000>, so it reads user-secrets. The host never migrates the database; `just start` or `just migrate` does (see Database migrations).

```bash
dotnet user-secrets set ConnectionStrings:Frappe \
  "Host=localhost;Database=frappe;Username=frappe_app;Password=app-local-only" --project src/Host/Frappe.Host
```

```bash
just run                              # or: just run api | worker | scheduler
dotnet run --project src/Host/Frappe.Host  # the same without Just
```

One binary runs every process role. Select it with `Platform:Role`, for example `--Platform:Role worker` or the environment variable `Platform__Role=worker`:

| Role | Purpose |
|---|---|
| `all` (default) | Everything in one process |
| `api` | HTTP API |
| `worker` | Background message processing |
| `scheduler` | Scheduled jobs |

Every role serves the health endpoints (see Health checks).

## Health checks

| Endpoint | Runs | Use as |
|---|---|---|
| `/health/live` | Only the trivial `self` check, never a dependency | Liveness probe: a failure restarts the process |
| `/health/startup` | Checks tagged `startup` and every critical dependency | Startup probe |
| `/health/ready` | Every critical dependency | Readiness probe: a failure takes the replica out of the load balancer |
| `/health` | Every check, as JSON with each status, duration and description (never exception text or check data) | Diagnosis; served only in the Development environment |

The probe endpoints answer the plain-text status: `Healthy` and `Degraded` return 200, `Unhealthy` returns 503.

An adapter for an external dependency registers its check with a criticality (`Frappe.Platform.Health`):

```csharp
builder.Services.AddHealthChecks()
    .AddDependency<PostgresHealthCheck>("postgres", Criticality.Critical)
    .AddDependency("valkey", Criticality.NonCritical, sp => new ValkeyHealthCheck(sp.GetRequiredService<IConnectionMultiplexer>()));
```

A critical dependency fails with `Unhealthy` and gates readiness and startup; a non-critical one fails with `Degraded` and never fails a probe. Each check gets a 2-second timeout unless one is passed, and a check reports its failure as `new HealthCheckResult(context.Registration.FailureStatus)`.

## Local environment

`compose.yaml` runs PostgreSQL, the migrations and the API image in containers, for local development only.

```bash
just start         # build the images, start PostgreSQL, apply pending migrations, then start the API
just migrate       # apply pending migrations to the running local database
just status        # each container and its health
just logs          # follow every container; or: just logs postgres | migrate | api
just stop          # stop and remove the containers; the database volume is kept
```

The API listens on <http://localhost:8080> (`/health/live`, `/health/startup`, `/health/ready`) and PostgreSQL on `localhost:5432`, both bound to the loopback interface only. The credentials in `compose.yaml` are throwaway local defaults; override `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `FRAPPE_OWNER_PASSWORD`, `FRAPPE_APP_PASSWORD`, `POSTGRES_PORT` or `API_PORT` in an untracked `.env` file. To delete the database as well, run `docker compose down --volumes`.

PostgreSQL runs `eng/postgres/init-roles.sh` only when its data volume is empty. It creates the two roles every environment needs: `frappe_owner` owns the database and applies migrations, and `frappe_app`, the role the host connects as, cannot bypass row-level security, owns nothing and reaches a table only through the grant of the migration that creates it. The superuser (`POSTGRES_USER`) exists only for that first-time init. A database volume created before the script existed has no such roles; recreate it with `just stop`, then `docker compose down --volumes`, then `just start`.

## Database migrations

EF Core migrations for `FrappeDbContext` live in `src/Migrations/Frappe.Migrations`, which is both the project and the startup project for `dotnet ef` (pinned in `.config/dotnet-tools.json`); no shipped project references it. Its design-time factory builds the same model as the host and holds no connection string, so commands that touch a database take the owner's through `--connection`. The history table is `platform.__ef_migrations_history`.

```bash
just add-migration AddOrders   # dotnet ef migrations add AddOrders --project src/Migrations/Frappe.Migrations --startup-project src/Migrations/Frappe.Migrations
just migrations-check          # dotnet ef migrations has-pending-model-changes (same project options)
```

Migrations are forward-only: delete the generated `Down` method. Each migration that creates a table grants `frappe_app` exactly the privileges it needs on it, and raw SQL lives in `.sql` files under `src/Migrations/Frappe.Migrations/Sql`, embedded and run with `migrationBuilder.Sql(EmbeddedSql.Read("<file>.sql"))`.

The host never applies migrations. The `migrations` target of the `Dockerfile` builds the image `frappe-migrations:local`, an [EF Core migrations bundle](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#bundles) that applies every pending migration and exits; run it as the owner role before starting a new version of the host:

```bash
docker run --rm frappe-migrations:local --connection "Host=...;Database=frappe;Username=frappe_owner;Password=..."
```

The image runs the host on the chiseled ASP.NET runtime as a non-root user, listening on port 8080; select the role with `Platform__Role` and pass the connection string as `ConnectionStrings__Frappe`, for example `docker run -e Platform__Role=worker -e ConnectionStrings__Frappe="Host=...;Database=frappe;Username=frappe_app;Password=..." frappe-api:local`.

## Repository layout

```text
src/
  Directory.Build.props       settings for shipped code only
  Host/Frappe.Host/           composition root
  Kernel/Frappe.Kernel/       types shared by every module
  Migrations/Frappe.Migrations/
                              EF Core migrations, the model snapshot and the design-time factory; never referenced by the host
  Platform/
    Directory.Build.props     exempts Platform from eng/BannedSymbols.OutsidePlatform.txt
    Frappe.Platform/          composes the platform projects into AddFrappePlatform and MapFrappeEndpoints
    Frappe.Platform.Health/   dependency health checks by criticality and the probe endpoints
    Frappe.Platform.Hosting/  process role (Platform:Role)
    Frappe.Platform.Persistence/
                              PostgreSQL data source, FrappeDbContext, its shared options and its critical health check
  Modules/<Name>/             one folder per module, holding that module's projects
tests/
  Directory.Build.props       settings for every test project
  Frappe.ArchitectureTests/   solution-wide architecture rules
  Frappe.TestUtilities/       fixtures the test projects share, such as the migrated PostgreSQL database
  Host/Frappe.Host.IntegrationTests/
  Kernel/Frappe.Kernel.UnitTests/
  Migrations/Frappe.Migrations.UnitTests/
  Platform/                   Frappe.Platform.*.UnitTests and .IntegrationTests
eng/                          analyzer configuration, banned APIs, code metrics, CI scripts and the PostgreSQL role init script
.github/                      CI workflows, Dependabot, labels and the pull request template
Dockerfile, compose.yaml      host and migrations images, and the local environment
```

The layout follows these rules:

- A grouping folder (`Host`, `Kernel`, `Platform`, `Modules/<Name>`, ...) never holds a `.csproj`; a project never sits inside another project's folder, because the SDK compiles every file below a project.
- A project's folder name, project name, assembly name and root namespace are the same.
- `tests/` mirrors `src/` path for path: the tests of `src/<Group>/<Project>` live in `tests/<Group>/<Project>.<Kind>Tests`, named `.UnitTests`, `.IntegrationTests` or `.ArchitectureTests`; only solution-wide tests and the shared `Frappe.TestUtilities` library sit at the root of `tests/`.
- Settings are scoped by subtree: each `Directory.Build.props` below the root imports its parent's with `GetPathOfFileAbove`, so a rule applies to a folder, never to a project name. Package versions live only in the root `Directory.Packages.props`.
- `Frappe.slnx` solution folders mirror the disk (`/src/Platform/`, `/tests/Platform/`, ...).

## License

Proprietary. See [LICENSE](LICENSE).
