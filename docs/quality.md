# Quality gates

- **Build:** warnings are errors; `latest-recommended` analysis, code style enforced in build; Sonar, Meziantou
  and Roslynator analyzers on every project.
- **Complexity** (`eng/CodeMetricsConfig.txt`): cyclomatic complexity 10 per method / 50 per type, maintainability
  index 20, class coupling 40 per method / 95 per type.
- **Coverage:** `just cover` fails under 70 % line coverage (`COVERAGE_MIN` overrides).
- **Mutation:** Stryker.NET on SharedKernel, Identity.Domain and Identity.Application, nightly; break threshold 50 %.

CI calls the same `justfile` recipes, so `just ci` reproduces the pipeline locally. Run `just` to list recipes.
Linters and scanners run from pinned Docker images. Reports land in `artifacts/`.

Tests build once and never rebuild: `just test` and `just cover` run `just build`, then launch every test
project's Microsoft.Testing.Platform executable in parallel and print each one's output whole. `just cover`
collects coverage during that single run and merges it, so CI runs each suite exactly once.

| Group | Recipes | Workflow |
| --- | --- | --- |
| dev | `setup`, `doctor`, `dev`, `seed`, `up` / `down`, `run`, `migrate`, `migration`, `codegen`, `clean` | — |
| quality | `fix` (dotnet format), `lint` (format check, actionlint, hadolint, gitleaks) | `ci.yml` |
| test | `build`, `test [fast\|integration\|all]`, `test-unit [filter]`, `test-arch [filter]`, `test-int [filter]`, `cover`, `mutate` | `ci.yml`, `nightly.yml` |
| security | `audit` (vulnerable NuGet packages, transitive included; secrets are scanned by `lint`); CodeQL weekly | `ci.yml`, `codeql.yml` |
| container | `image`, `image-scan` (trivy, HIGH/CRITICAL) | `ci.yml` |
| git | `commits [base] [head]`, `branch [name]`, `hooks`, `test-git` (tests the hygiene scripts) | `pr-hygiene.yml` |
| ci | `ci` (lint, test-git, cover (build + every suite), audit, image, image-scan) | — |

Dependabot updates NuGet, Actions and Docker base images weekly, with Conventional Commit titles.

