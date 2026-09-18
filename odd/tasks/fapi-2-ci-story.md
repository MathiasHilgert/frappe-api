# FAPI-2 — platform: Tell the CI story and add free security checks

Plane: [FAPI-2](https://app.plane.so/nulled-software/browse/FAPI-2/) (module platform). Branch: `ci/fapi-2-ci-story`.

## Objective
Every PR tells a readable story of what was verified, and known risks are blocked before merge.

## Scope
Storytelling step names, `check` split into visible steps, job summary (results table + Modulith diagram), test report annotations, PR title check (Conventional Commits), CodeQL (Java), Dependency Review, Dependabot (Gradle, Actions, Docker), least-privilege permissions, concurrency, timeouts, README CI section.

Out of scope: image build/deploy, coverage thresholds, Flyway migration tests.

## TDD
Strict TDD enabled; no production code in this ticket. Verification is observing workflows on a real PR.

## Tasks
- [x] T1 Storytelling CI workflow with split steps, summary and test annotations, hygiene
- [x] T2 PR title check
- [x] T3 CodeQL + Dependency Review
- [x] T4 Dependabot config
- [x] T5 README CI section
- [ ] T6 PR opened; all checks observed

## Progress / evidence
- T1: rewrote `.github/workflows/ci.yml` — job `secrets` (gitleaks with full history), job `quality-gate` (steps "Verify formatting" → spotlessCheck, "Verify module boundaries" → `test --tests ModularityTests`, "Run the test suite against real Postgres" → `test`; together equal `./gradlew check`, which stays the local command). Added `mikepenz/action-junit-report@v5` PR annotations (checks: write only on that job), `$GITHUB_STEP_SUMMARY` table + Modulith docs artifact upload (link, not embedded mermaid — Documenter emits PlantUML/AsciiDoc, no reliable mermaid conversion available in-workflow). Hygiene: top-level `permissions: contents: read`, per-job elevated grants only where needed, `concurrency` per ref with cancel-in-progress on PRs, `timeout-minutes` on every job, actions pinned to major versions. Commit `379e09f`.
- T2: `.github/workflows/pr-title.yml`, `amannn/action-semantic-pull-request@v5` on `pull_request` opened/edited/synchronize/reopened, `pull-requests: read`. Commit `e6e4c46`.
- T3: `.github/workflows/codeql.yml` — `analyze` job (java-kotlin, build-mode manual + `./gradlew compileJava` under Java 25, `security-events: write`) on push to main, PRs and weekly cron; `dependency-review` job (`actions/dependency-review-action@v4`, `fail-on-severity: high`) on PRs only. Commit `df360b9`.
- T4: `.github/dependabot.yml` — ecosystems `gradle` (/), `github-actions` (/), `docker-compose` (/, covers `compose.yaml`; confirmed supported via GitHub docs), weekly, minor/patch grouped, Conventional Commit prefixes (`build(deps)`, `ci(deps)`). Commit `988ea8d`.
- T5: README "Continuous integration" section + colorless mermaid flowchart of the check pipeline. Commit `b5a4897`.
- Verification so far: `actionlint .github/workflows/*.yml` (mise-installed 1.7.12) — no findings. `./gradlew check` — BUILD SUCCESSFUL (no production code touched, UP-TO-DATE).

## Next step
T6: push branch, open PR, watch checks, fix bounded failures (≤3 attempts), confirm job summary and CodeQL ran.
