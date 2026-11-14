# Contributing to Frappé API

This repository is proprietary (see [LICENSE](../LICENSE)): contributions are made by people the owner has authorized
in writing. This guide covers how work gets into `main`: the commands, the tests, the conventions and the checks
every pull request must pass.

## Quick path

1. Branch from `main` as `<type>/<kebab-topic>`, for example `feat/health-probes`.
2. Make the change with its tests; run `just build` and `just test all`.
3. If you changed an endpoint or an integration event on purpose, run `just contracts update` and commit the result.
4. Open a pull request titled `<type>(<scope>): <summary>` and fill in the
   [template](pull_request_template.md).
5. Wait for every required check to pass; `main` is squash-merged.

## Repository layout

The root keeps only what .NET and Git look for there; every other file lives where its tool discovers it, or where
the workflows and the `.justfile` point it.

```text
.config/dotnet-tools.json   local .NET tools (dotnet-ef), found by `dotnet tool restore`
.github/                    workflows, their scripts, Renovate (renovate.json5), this guide, the PR template
deploy/                     Dockerfile (+ Dockerfile.dockerignore), compose.yaml, .env.example, observability/
docs/                       documentation; api/ holds the committed OpenAPI and event contracts, adr/ the decisions
eng/                        build and lint inputs: just modules, CodeMetricsConfig.txt, typos, markdownlint, hadolint, squawk
src/                        Platform/, Modules/<Module>/, Hosts/
tests/                      mirrors src/: Platform/, Modules/<Module>/ (UnitTests, IntegrationTests), plus the
                            cross-cutting Architecture and EndToEnd suites and Shared/ (helpers, no tests)
tools/                      seeds/ (reference data generators) and bruno/ (a collection that exercises the API)
.justfile                   the `just <area> <action>` entry point
Directory.Build.props       build settings and analyzers for every project
Directory.Packages.props    every package version (central package management)
Frappe.slnx                 the solution
global.json                 the pinned SDK
NuGet.config                nuget.org only, with package source mapping
```

## Commands

Commands are `just <area> <action>`; `just` lists them all.

| Area | Commands |
|------|----------|
| `stack` | `up`, `down`, `reset` (deletes the volumes), `ps`, `health`, `logs <service>` |
| `infra` | `up`: only the infrastructure and LGTM, to run the hosts from here or the IDE |
| `db` | `migrate`: roles, migrations, Quartz tables and grants |
| `run` | `api` (on port 5080), `worker`: the hosts on this machine against the compose infrastructure |
| `test` | `all` (`platform`, `architecture` and `geo`), `platform`, `architecture`, `geo` (`geo-unit` then `geo-integration`), `geo-unit` (no Docker, seconds), `geo-integration` (needs Docker for Testcontainers), `e2e` (not in `all`: builds the image and runs it end to end, a few minutes) |
| `docker` | `image`: the one image for the Api, the Worker and the Migrator |
| `contracts` | `update`: regenerate `docs/api/` (the HTTP and event contracts) after a deliberate change |
| `deps` | `lock`: rewrite every `packages.lock.json` after changing a package or the SDK; `audit`: list vulnerable packages |
| `github` | `conventions`: test the title, commit and branch rules; `protect`: apply the repository settings and the ruleset of `main`; `labels`: make the labels match `.github/labels.json` |

`just build` builds the solution.

## Testing

| Suite | Project | Command | Needs |
|-------|---------|---------|-------|
| Platform capabilities | `tests/Platform/Frappe.Platform.Tests` | `just test platform` | Nothing |
| Architecture rules | `tests/Frappe.Architecture.Tests` | `just test architecture` | Nothing |
| Geo unit | `tests/Modules/Geo/Frappe.Geo.UnitTests` | `just test geo-unit` | Nothing; runs in seconds |
| Geo integration | `tests/Modules/Geo/Frappe.Geo.IntegrationTests` | `just test geo-integration` | Docker (Testcontainers: Postgres, Valkey) |
| End to end | `tests/Frappe.EndToEnd.Tests` | `just test e2e` | Docker; a few minutes |

`just test all` runs every suite but the end-to-end one. Each suite runs under a timeout.

`tests/` mirrors `src/`: a module's tests live under `tests/Modules/<Module>/`, the platform's under `tests/Platform/`.
A module has two suites. A test goes to `*.UnitTests` when it needs no Docker, no database, no broker, no host and no
network: the domain, use cases against fakes, and adapters against scripted HTTP handlers. Anything else goes to
`*.IntegrationTests`, and so does a class that mixes both. The unit project references no Testcontainers package and no
host, so a test that needs one does not compile there. Test doubles both suites use live in a small library next to
them (`Frappe.<Module>.Testing`); helpers any suite may use (`RepositoryRoot`, `CapturedLogs`) live in
`tests/Shared/Frappe.Testing`. `tests/Directory.Build.props` makes every `*Tests` project a TUnit app, so a test
`.csproj` holds only its references.

`just test e2e` tests what gets deployed: it builds the image from `deploy/Dockerfile` (BuildKit) and runs Postgres, NATS,
Valkey, the Migrator, the Api and the Worker as containers, wired like `deploy/compose.yaml`.

The [Bruno collection](../tools/bruno/README.md) exercises a running Api from the outside, request by request.

## Conventions

- **Titles and commits.** `main` is squash-merged, so the pull request title becomes the commit:
  `<type>(<scope>): <summary>`, scope mandatory, `!` for a breaking change. Commits follow the same rule, the branch
  is `<type>/<kebab-topic>` with the title's type, and no AI attribution is allowed anywhere. The rules live in
  `.github/scripts/conventions.mjs` (`just github conventions` tests them).
- **Pull requests.** Follow the [template](pull_request_template.md): a TL;DR told as a story, then the
  sections that describe the change (architecture, infrastructure, events, observability, endpoints, decisions, how
  to verify, risk).
- **Complexity budget.** Every production method stays at cognitive complexity 15 or less (Sonar S3776) and cyclomatic
  complexity 10 or less (CA1502). Both are analyzer rules, so a method over budget fails the build on your machine too.
  Warnings are errors.
- **Contracts.** `docs/api/openapi/v1.json` and `docs/api/events/v1.json` are generated from the code and a test fails
  when they are stale (`just contracts update`). CI compares them with the base branch through oasdiff: a breaking
  change needs `!` in the title.
- **Migrations.** A model change without its migration fails; the SQL of new migrations is linted by Squawk for locks
  and data loss (a deliberate risky change needs the `risky migration approved` label); and the base branch's database
  is upgraded with this branch's Migrator.
- **Coverage.** At least 80% of the production lines a pull request changes are covered by the suites.
- **Decisions.** Architecture decision records live in [`docs/adr/`](../docs/adr/).

## Continuous integration

Every pull request runs five workflows. Each check is named after what it guarantees, and every one of them is
required by the ruleset of `main` (`.github/rulesets/main.json`, applied with `just github protect`, which also turns on
the dependency graph that the dependency review needs). A pull request runs only the checks its changes can affect:
see **Path-aware checks** below.

| Workflow | Checks |
|----------|--------|
| Pull request | Title follows Conventional Commits · Commits follow Conventional Commits · Branch name is valid · Assignee and labels set |
| Code quality | Code is formatted · Code compiles without warnings · Complexity is within limits |
| Tests | Unit tests pass · Architecture rules hold · Integration tests pass · Changed lines are covered · App starts and responds |
| Compatibility | API has no unmarked breaking changes · Events have no unmarked breaking changes · Database migrations are safe |
| Security | No vulnerable dependencies · No leaked secrets · Code has no known vulnerabilities · Workflows are secure · Container image is clean |

"Assignee and labels set" assigns the author and sets the `size:` and `type:` labels itself.

- **Path-aware checks.** A "Changes" job (`.github/workflows/changes.yml`) sorts the changed files into categories
  (`.github/scripts/changes.mjs`, pinned by its tests), and a check whose inputs did not change is skipped through its
  job's `if:`, which GitHub reports as passing, so a README-only pull request runs no build, suite or scan. The
  workflows themselves are never path-filtered: a skipped workflow would leave its required checks waiting forever.
  The checks on the pull request itself, "Code is formatted" (whose C# steps are gated), "No leaked secrets" and
  "Workflows are secure" always run. Everything runs for a change to the CI, a file in no category, an incomplete file
  list or a failed detection, and on `main`, Nightly, Release and the weekly security scan.
- **Nightly** rebuilds everything without caches.
- **Release** keeps a release pull request (release-please) from the Conventional Commits; merging it tags the
  version and publishes the image to GHCR with SBOM and build provenance. Versions stay 0.x until a deliberate
  1.0.0. The release pull request gets its checks only when a GitHub App opens it (a pull request opened by
  `GITHUB_TOKEN` triggers no workflow): create an app owned by you, with repository permissions Contents, Pull requests
  and Issues set to read and write and no webhook, install it on this repository only, then set the repository
  variable `RELEASE_APP_CLIENT_ID` to its client ID and the secret `RELEASE_APP_PRIVATE_KEY` to a private key it
  generates. Without the app, an admin merges the release pull request through the ruleset's bypass.
- **Dependencies** are updated by Renovate (`.github/renovate.json5`), lock files included, once the Renovate app is installed.
  After changing a package or the SDK by hand, run `just deps lock`.
- Planned: mutation testing. Stryker.NET 5.0.0 cannot analyze `net11.0` projects yet.
