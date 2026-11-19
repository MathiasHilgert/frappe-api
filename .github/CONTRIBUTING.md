# Contributing to Frappé API

This repository is proprietary (see [LICENSE](../LICENSE)): contributions are made by people the owner has authorized
in writing. This guide covers how work gets into `main`: the commands, the conventions and the checks every pull
request must pass.

## Quick path

1. Branch from `main` as `<type>/<kebab-topic>`, for example `feat/health-probes`.
2. Make the change with its tests; run `just build` and `just test all`, then `just pr-check` before pushing.
3. Keep the pull request to one idea: at most about 400 lines of production code plus its tests. Each pull request
   builds green and reverts alone.
4. Open a pull request titled `<type>(<scope>): <summary>` and fill in the [template](pull_request_template.md).
5. Wait for every required check to pass; `main` is squash-merged and the pull request body becomes the commit body.

## Repository layout

The root keeps only what .NET and Git look for there; every other file lives where its tool discovers it, or where
the workflows and the `.justfile` point it.

```text
.config/dotnet-tools.json   local .NET tools, found by `dotnet tool restore`
.github/                    workflows, their scripts, labels, Renovate (renovate.json5), this guide, the PR template
eng/                        build and lint inputs: just modules, CodeMetricsConfig.txt, analyzers.globalconfig,
                            BannedSymbols*.txt, spectral, typos, markdownlint, hadolint, squawk, scaffolding
src/                        Platform/ (shared libraries) and, as they arrive, Hosts/ and Modules/<Module>/
tests/                      mirrors src/, plus the cross-cutting Architecture suite and Shared/ (helpers, no tests)
.justfile                   the `just <area> <action>` entry point
Directory.Build.props       build settings and analyzers for every project
Directory.Packages.props    every package version (central package management)
Frappe.slnx                 the solution
global.json                 the pinned SDK
NuGet.config                nuget.org only, with package source mapping
```

## Commands

Commands are `just <area> <action>`; `just` lists them all. A recipe whose subject does not exist yet prints a skip.

| Area | Commands |
|------|----------|
| `build`, `format` | build the solution (warnings are errors); fix whitespace, style and analyzer findings |
| `pr-check` | run locally the checks a pull request most often fails: conventions, build, format, model, contracts, schema dumps |
| `test` | `all`, `platform` (`platform-unit` then `platform-integration`), `architecture`, `host` |
| `db` | `migrate` and `schema`: regenerate each module's committed `schema.sql` |
| `contracts` | `update`: regenerate `contracts/` (the HTTP and event contracts) after a deliberate change |
| `deps` | `lock`: rewrite every `packages.lock.json` after changing a package or the SDK; `audit`: list vulnerable packages |
| `scaffold` | `command`, `query`, `error`, `event`, `endpoint`, `bruno`: the repetitive skeleton of a use case |
| `github` | `conventions`: test the title, commit and branch rules and the change map; `protect`: apply the repository settings and the ruleset of `main`; `labels`: make the labels match `.github/labels.json` |

## Conventions

- **Titles and commits.** `main` is squash-merged, so the pull request title becomes the commit:
  `<type>(<scope>): <summary>`, scope mandatory and lower-case (also for `ci` and `build`), header at most 100
  characters, `!` for a breaking change. Commits follow the same rule, the branch is `<type>/<kebab-topic>` with the
  title's type, and no AI attribution is allowed anywhere. The rules live in `.github/scripts/conventions.mjs`
  (`just github conventions` tests them).
- **Pull requests.** Follow the [template](pull_request_template.md): a TL;DR told as a story, then the sections that
  describe the change (architecture, infrastructure, events, observability, endpoints, decisions, how to verify, risk).
- **Size.** The `size:` label counts authored lines only: lock files, committed contracts and generated migration files
  do not count.
- **Complexity budget.** Every production method stays at cognitive complexity 15 or less (Sonar S3776) and cyclomatic
  complexity 10 or less (CA1502). Both are analyzer rules, so a method over budget fails the build on your machine too.
  Warnings are errors.
- **Analyzers.** Besides the SDK's, Sonar and StyleCop: banned APIs (`eng/BannedSymbols.txt` everywhere,
  `eng/BannedSymbols.Production.txt` outside `tests/`, rule RS0030), and only the Meziantou and Roslynator rules
  `eng/analyzers.globalconfig` turns on.

## Continuous integration

Every pull request runs the workflows below. Each check is named after what it guarantees, and every one of them is
required by the ruleset of `main` (`.github/rulesets/main.json`, applied with `just github protect`, which also turns on
the dependency graph that the dependency review needs). A check never stays pending: when its subject does not exist
yet (no Dockerfile, no migrations, no contracts), or its inputs did not change, its job is skipped, which GitHub
reports as passing.

| Workflow | Checks |
|----------|--------|
| Pull request | Title follows Conventional Commits · Commits follow Conventional Commits · Branch name is valid · Assignee and labels set |
| Code quality | Code is formatted · Code compiles without warnings · Complexity is within limits |
| Security | No vulnerable dependencies · No leaked secrets · Code has no known vulnerabilities · Workflows are secure · Container image is clean |

"Assignee and labels set" assigns the author and sets the `size:` and `type:` labels itself.

- **Path-aware checks.** A "Changes" job (`.github/workflows/changes.yml`) lists which subjects exist in the
  repository and sorts the changed files into categories (`.github/scripts/changes.mjs`, pinned by its tests). A
  check whose subject is missing, or whose inputs did not change, is skipped through its job's `if:`. The workflows
  themselves are never path-filtered: a skipped workflow would leave its required checks waiting forever. "Code is
  formatted" (whose C# steps are gated), "No leaked secrets" and "Workflows are secure" always run. Everything that
  exists runs for a change to the CI, a file in no category, an incomplete file list or a failed detection, and on
  `main` and the weekly security scan.
- **Dependencies** are updated by Renovate (`.github/renovate.json5`), lock files included, once the Renovate app is
  installed. After changing a package or the SDK by hand, run `just deps lock`.
