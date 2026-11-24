# Contributing to Frappé API

This repository is proprietary (see [LICENSE](LICENSE)): contributions are made by people the owner has authorized
in writing. This guide covers how work gets into `main`: the commands, the tests, the conventions and the checks
every pull request must pass.

## Quick path

1. Branch from `main` as `<type>/<kebab-topic>`, for example `feat/health-probes`.
2. Make the change with its tests; run `just build` and `just test all`.
3. If you changed an endpoint or an integration event on purpose, run `just contracts update` and commit the result.
4. Open a pull request titled `<type>(<scope>): <summary>` and fill in the
   [template](.github/pull_request_template.md).
5. Wait for every required check to pass; `main` is squash-merged.

## Repository layout

The root keeps only what .NET and Git look for there; every other file lives where its tool discovers it, or where
the workflows and the `.justfile` point it.

```text
.config/dotnet-tools.json   local .NET tools (dotnet-ef), found by `dotnet tool restore`
.github/                    workflows, their scripts and actions, Renovate (renovate.json5), the PR template
contracts/                  the committed OpenAPI (openapi/) and integration event (events/) contracts
deploy/                     Dockerfile (+ Dockerfile.dockerignore), compose.yaml, .env.example, observability/
eng/                        build and lint inputs: just modules, CodeMetricsConfig.txt, analyzers.globalconfig,
                            BannedSymbols*.txt, spectral.yaml, typos, markdownlint, hadolint, squawk
src/                        Platform/, Modules/<Module>/ (as modules arrive), Hosts/
tests/                      mirrors src/: Platform/, Modules/<Module>/ (UnitTests, IntegrationTests), Hosts/ (the
                            Api composed with its modules), plus the cross-cutting Architecture and EndToEnd
                            suites and Shared/ (helpers, no tests)
tools/                      bruno/ (a collection that exercises the API)
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
| `db` | `migrate`: roles, migrations, Quartz tables and grants, `schema`: regenerate each schema's committed `schema.sql` |
| `run` | `api` (on port 5080), `worker`: the hosts on this machine against the compose infrastructure |
| `test` | `all` (`platform`, `architecture` and `host`), `platform` (`platform-unit` then `platform-integration`), `platform-unit` (no Docker, seconds), `platform-integration` (needs Docker for Testcontainers), `architecture`, `host` (the Api composed with its modules, no Docker), `e2e` (not in `all`: builds the image and runs it end to end, a few minutes) |
| `docker` | `image`: the one image for the Api, the Worker and the Migrator |
| `contracts` | `update`: regenerate `contracts/` (the HTTP and event contracts) after a deliberate change |
| `deps` | `lock`: rewrite every `packages.lock.json` after changing a package or the SDK; `audit`: list vulnerable packages |
| `scaffold` | `command`, `query`, `error`, `event`, `endpoint`, `bruno`: the repetitive skeleton of a use case (see below) |
| `github` | `conventions`: test the title, commit and branch rules; `protect`: apply the repository settings and the ruleset of `main`; `labels`: make the labels match `.github/labels.json` |

`just build` builds the solution.

### Scaffolding

`just scaffold <kind>` writes the skeleton every use case repeats, so you write only the logic and the tests. Each file
comes from a `dotnet new` template in `eng/templates`; what has to be appended to an existing file is done by
`eng/scaffold/scaffold.sh`, and what it cannot guess it prints as a next step. Every skeleton compiles under the
analyzers, with placeholders that start with `FILL IN` for you to replace.

| Kind | Writes |
|------|--------|
| `command <Module> <Area> <UseCase>` | the command, its handler (a note to authorize first when the module has an `*Access` class) and a unit test that fails until the logic exists |
| `query <Module> <Area> <UseCase>` | the same for a query |
| `error <Module> <code> <kind> "<en>" "<es>" "<pt-BR>"` | the code constant, the static error and the three messages (`kind`: `notfound`, `forbidden`, `conflict`, `invalid`, `rule`) |
| `event <Module> <Area>/<Name> "Field:Type,..."` | the integration event in Contracts, the domain event and the arm of the module's `*IntegrationEvents` |
| `endpoint <Module> <Area> <UseCase> <verb> <route>` | the endpoint mapping (you map it in the module's `*Http.cs`) |
| `bruno <folder> "<name>" <method> <url> [status]` | a Bruno request with the next free `seq` of its folder |

## Testing

| Suite | Project | Command | Needs |
|-------|---------|---------|-------|
| Platform unit | `tests/Platform/Frappe.Platform.UnitTests` | `just test platform-unit` | Nothing |
| Platform integration | `tests/Platform/Frappe.Platform.IntegrationTests` | `just test platform-integration` | Docker (Testcontainers) |
| Architecture rules | `tests/Frappe.Architecture.Tests` | `just test architecture` | Nothing |
| Api host (the HTTP contract) | `tests/Hosts/Frappe.Api.IntegrationTests` | `just test host` | Nothing; runs in seconds |
| End to end | `tests/Frappe.EndToEnd.Tests` | `just test e2e` | Docker; a few minutes |

### Reusing test containers

Each integration class starts its containers, which dominates a filtered run. With `FRAPPE_TEST_REUSE=1` in your shell
(local only: CI never sets it and `CI=true` turns it off), the session-wide Postgres, Valkey and NATS
containers are kept and restarted by the next run. All of them are routed through `TestReuse` in
`tests/Shared/Frappe.Testing.Containers`:

- A container is found again by a label made of the worktree, the test app and the fixture, so worktrees never share one.
- Credentials are derived instead of random (the container's configuration must not change between runs).
- A reused Postgres has every non-system database dropped at fixture start, so a crashed run leaves nothing behind; the
  template is rebuilt each run, so a changed migration is picked up. Run only one test session per test app per
  worktree at a time under reuse (two share one container and the reset of one clobbers the other);
  unset the variable for parallel runs.
- Valkey and NATS keep their data: tests already isolate by instance name, bucket or namespace.
- Ryuk needs no setting: it does not own a reused container. `just test reuse-clean` removes them (also after changing a
  fixture's image or settings). The end-to-end suite always starts fresh.

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

The Bruno collection (`tools/bruno`) exercises a running Api from the outside, request by request.

## Conventions

- **Titles and commits.** `main` is squash-merged, so the pull request title becomes the commit:
  `<type>(<scope>): <summary>`, scope mandatory, `!` for a breaking change. Commits follow the same rule, the branch
  is `<type>/<kebab-topic>` with the title's type, and no AI attribution is allowed anywhere. The rules live in
  `.github/scripts/conventions.mjs` (`just github conventions` tests them).
- **Pull requests.** Follow the [template](.github/pull_request_template.md): a TL;DR told as a story, then the
  sections that describe the change (architecture, infrastructure, events, observability, endpoints, decisions, how
  to verify, risk).
- **Complexity budget.** Every production method stays at cognitive complexity 15 or less (Sonar S3776) and cyclomatic
  complexity 10 or less (CA1502). Both are analyzer rules, so a method over budget fails the build on your machine too.
  Warnings are errors.
- **Analyzers.** Besides the SDK's, Sonar and StyleCop: banned APIs (`eng/BannedSymbols.txt` everywhere,
  `eng/BannedSymbols.Production.txt` outside `tests/`, rule RS0030), and only the Meziantou and Roslynator rules
  `eng/analyzers.globalconfig` turns on. A `*.UnitTests` project that references a database, NATS or Valkey package
  fails the build: those tests belong in the integration suite.
- **Contracts.** `contracts/openapi/v1.json` and `contracts/events/v1.json` are generated from the code and a test fails
  when they are stale (`just contracts update`). CI compares them with the base branch through oasdiff: a breaking
  change needs `!` in the title. Spectral lints the HTTP contract against the REST rules in `eng/spectral.yaml`.
- **Migrations.** A model change without its migration fails; the SQL of new migrations is linted by Squawk for locks
  and data loss (a deliberate risky change needs the `risky migration approved` label); and the base branch's database
  is upgraded with this branch's Migrator. Each schema is committed as `schema.sql` (a `pg_dump
  --schema-only`, so raw-SQL objects EF does not model are covered): a migration PR regenerates it with `just db schema`,
  and CI fails on any difference, for a fresh database and for the upgraded one.
- **Coverage.** At least 80% of the production lines a pull request changes are covered by the suites.

## Continuous integration

Every pull request runs one workflow, `ci.yml`, plus a small one that repeats the checks reading the title and the
labels when those are edited (`pull-request-metadata.yml`, under the same check names). Each check is named after what it
guarantees, and every one of them is required by the ruleset of `main` (`.github/rulesets/main.json`, applied with
`just github protect`, which also turns on the dependency graph that the dependency review needs):

| Group | Checks |
|-------|--------|
| The pull request | PR follows the conventions (title, commits and branch) · Assignee and labels set |
| Code | Code is formatted · Code compiles without warnings · Complexity is within limits |
| Tests | Unit tests pass · Architecture rules hold · Integration tests pass · Changed lines are covered |
| The running app | App starts and responds · Bruno collection passes |
| Compatibility | API contract is sound (breaking changes, REST rules, events) · Database migrations are safe |
| Security | No vulnerable dependencies · No leaked secrets · Workflows are secure · Container image is clean |

"Assignee and labels set" assigns the author and sets the `size:` and `type:` labels itself. Adding the
`risky migration approved` label does not re-run the migration check by itself: re-run that job, which reads the labels
when it runs.

- **One change detection.** The job "Detect what the changes affect" sorts the changed files into categories
  (`.github/scripts/changes.mjs`, pinned by its tests) and looks for what exists to be checked. A check whose inputs did
  not change, or whose subject does not exist yet (no projects, no image, no migrations, no contracts, no Bruno requests),
  is skipped through its job's `if:`, which GitHub reports as passing, never pending, so a README-only pull request runs
  no build, suite or scan. The workflow itself is never path-filtered: a skipped workflow would leave its required checks
  waiting forever. "Code is formatted" (whose C# steps are gated), "No leaked secrets" and "Workflows are secure" always
  run. Everything runs for a change to the CI, a file in no category, an incomplete file list or a failed detection, and
  on `main`. The image scan runs only when the Dockerfile, the compose stack or the dependencies (lock files, package
  versions, SDK, NuGet sources) change, so a C# change does not rescan it.
- **Suites are found by name**: every `*.UnitTests`, `*.IntegrationTests` and `*.Architecture.Tests` project under
  `tests/` gets its own job, so a new suite needs no workflow edit. Each job builds only what it needs.
- **One compile.** "Code compiles without warnings" writes a binary log, reads it back for the complexity findings and
  uploads only those, which "Complexity is within limits" reads.
- **Line length.** `.github/scripts/line-length.mjs` fails a line over 150 characters in C# files and Markdown prose
  (fenced code, tables and bare URLs are exempt); `just pr-check` runs it too.
- **Security on `main`.** CodeQL runs on every push to `main` and weekly, together with an audit of the packages, not
  as a pull request check.
- **Nightly** rebuilds everything without caches.
- **Image retention** (weekly) prunes `ghcr.io/<owner>/frappe-api`: `sha-*` images older than 30 days and untagged
  versions older than 7 days go; release tags (`<version>`, `<major>.<minor>`, `latest`) are never deleted. A manual
  run is a dry run unless its `dry-run` input is switched off.
- **Release** keeps a release pull request (release-please) from the Conventional Commits; merging it tags the
  version and publishes the image to GHCR with SBOM and build provenance, sharing the layer cache of the CI builds.
  Versions stay 0.x until a deliberate 1.0.0. The release pull request gets its checks only when a GitHub App opens it (a pull request opened by
  `GITHUB_TOKEN` triggers no workflow): create an app owned by you, with repository permissions Contents, Pull requests
  and Issues set to read and write and no webhook, install it on this repository only, then set the repository
  variable `RELEASE_APP_CLIENT_ID` to its client ID and the secret `RELEASE_APP_PRIVATE_KEY` to a private key it
  generates. Without the app, an admin merges the release pull request through the ruleset's bypass.
- **Dependencies** are updated by Renovate (`.github/renovate.json5`), lock files included, once the Renovate app is installed.
  After changing a package or the SDK by hand, run `just deps lock`.
- Planned: mutation testing. Stryker.NET 5.0.0 cannot analyze `net11.0` projects yet.
