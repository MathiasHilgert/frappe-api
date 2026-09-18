# FAPI-1 — platform: Set up repository tooling and quality gates

Plane: FAPI-1 (module platform). Branch: `chore/fapi-1-repo-setup`.

## Objective
Give every following ticket a consistent, checked baseline: licensing, formatting, secret scanning, module boundary enforcement, local infra and CI, all behind a single `./gradlew check` gate.

## Scope
Proprietary LICENSE, README, .gitignore, Spotless + Palantir Java Format, gitleaks (pre-commit + CI), compose.yaml with pinned Postgres + NATS JetStream, Modulith `ApplicationModules.verify()` test, `./gradlew check` as the single gate, GitHub Actions on PRs with Gradle cache, first Conventional Commit.

Out of scope: NATS adapter / outbox relay, domain module code, deploy (Kamal). Creating the GitHub remote and pushing needs explicit user confirmation.

## TDD
Strict TDD enabled (session config). Runner: `./gradlew test` (JUnit 5). Tooling task: RED applies to the Modulith verification test (write it, observe it run), not to config files.

## Tasks
- [x] T1 Repo hygiene: LICENSE (All rights reserved), README, .gitignore (exclude `.atl/`)
- [x] T2 Spotless + Palantir wired into `check`; sources formatted
- [x] T3 Modulith verification test + documentation test passing
- [x] T4 compose.yaml with pinned Postgres and NATS JetStream; app boots against it
- [x] T5 gitleaks pre-commit hook (versioned hook dir) + config
- [x] T6 GitHub Actions: gitleaks + `./gradlew check` with Gradle cache
- [x] T7 First commit(s) with Conventional Commits

## Checks
`./gradlew check`, `docker compose up -d` + app boot, gitleaks blocks a planted fake secret.

## Progress / evidence

### T1 — Repo hygiene
- Added `LICENSE` ("Copyright (c) 2026 Frappé. All rights reserved.", proprietary, public-for-transparency-only note).
- Added `README.md` (product summary, license note, prerequisites, docker compose, `./gradlew check`, hooks setup).
- Added `.gitignore` entry for `.atl/` (tool dir, must never be committed).
- Commit: `49cdf9e chore: add license, readme and gitignore`.

### T2 — Spotless + Palantir Java Format
- Added `com.diffplug.spotless` 7.2.1 plugin with `palantirJavaFormat()` (default bundled version), wired `check` to depend on `spotlessCheck`.
- **Deviation / finding (documented, not silently worked around):** `palantir-java-format` (any version tried, including an explicit 2.66.0 pin) throws `NoSuchMethodError` on `com.sun.tools.javac...Log$DeferredDiagnosticHandler.getDiagnostics()` when the **Gradle daemon JVM itself** is Temurin 25 — reproduced consistently, independent of the compile toolchain. It works cleanly when the Gradle daemon runs on Temurin 21, while the app's Java toolchain (`languageVersion.of(25)`) still compiles/runs/tests on Temurin 25 via the `org.gradle.toolchains.foojay-resolver-convention` plugin added to `settings.gradle.kts` (auto-provisioning).
  - Local dev and CI must run `./gradlew` with `JAVA_HOME` pointed at Temurin 21; documented in README "Prerequisites".
  - CI workflow (T6) therefore uses `actions/setup-java` with `java-version: 21` (not 25) to run Gradle, relying on toolchain auto-provisioning for 25. This deviates from the ticket's literal "temurin 25" instruction for setup-java; the reason and evidence are recorded here and in the workflow file comment.
- `gradle.properties`: added `--add-exports`/`--add-opens` JVM args required for `palantir-java-format`'s reflective javac access under the module system.
- Verified: `JAVA_HOME=<temurin-21> ./gradlew spotlessApply check -x test` → `BUILD SUCCESSFUL`.
- Commits: `a5371d0 build: enforce palantir format with spotless`, `3fc6bd5 chore: add gradle wrapper`.

### T3 — Modulith verification test (strict TDD)
- Wrote `src/test/java/com/frappe/ModularityTests.java`: `ApplicationModules.of(FrappeApiApplication.class).verify()` plus a `Documenter(modules).writeDocumentation()` test.
- Ran it: `./gradlew test --tests "com.frappe.ModularityTests"` → passed immediately (GREEN) on first run. **Honest note on TDD RED**: with only the default single package (no module boundaries declared yet), there is nothing for `verify()` to violate, so no RED state was observable — recorded here rather than fabricating one. `Documenter` output confirmed generated at `build/spring-modulith-docs/{all-docs.adoc,components.puml}`.
- Foojay resolver plugin version bumped 0.9.0 → 1.0.0 (0.9.0 incompatible with Gradle 9.7.1: `JvmVendorSpec` missing `IBM_SEMERU`).
- After Spotless reformatted the file (tabs → spaces per Palantir style), re-verified `./gradlew check` still green.
- Commits: `6c95c42 test: verify module boundaries with spring modulith`, `2d38d65 style: apply palantir formatting to modularity test`.

### T4 — compose.yaml (pinned Postgres + NATS JetStream)
- `postgres:18-alpine`, db/user/password `frappe` (password overridable via `POSTGRES_PASSWORD` env, dev-only default), healthcheck via `pg_isready`.
- `nats:2.12-alpine`, JetStream enabled (`-js`), monitoring endpoint enabled (`-m 8222`, required for the healthcheck — the default alpine image does not expose the monitoring HTTP endpoint without it), healthcheck via `/healthz`.
- Verified: `docker compose up -d` → both services reach `healthy`. App boot verified via `./gradlew bootRun` (random port) → log line `Started FrappeApiApplication in 2.657 seconds` with no errors, Spring Boot Docker Compose support auto-connected to the running Postgres container.
- `docker compose down` cleans up.
- Commit: `a2f1032 chore: pin postgres and nats in compose`.

### T5 — gitleaks pre-commit hook
- `.githooks/pre-commit` (versioned, executable): runs `gitleaks git --pre-commit --staged --redact --verbose --config .gitleaks.toml`, fails with a clear message if gitleaks is not installed.
- `.gitleaks.toml`: extends gitleaks' default ruleset.
- `git config core.hooksPath .githooks` run locally; documented in README "Git hooks" as a one-time per-clone step.
- **Verification (planted secret):** first attempt used the textbook AWS example key (`AKIAIOSFODNN7EXAMPLE`) — gitleaks' default allowlist recognizes it as a doc placeholder and let it through (`no leaks found`). Committed and immediately reverted (`git reset --soft HEAD~1`, file removed) — no fake secret was ever left in history. Retried with a random fake GitHub PAT (`ghp_<36 random chars>`) in a scratch file: hook correctly **blocked** the commit (`RuleID: github-pat`, `leaks found: 1`, commit aborted). Scratch file deleted and unstaged afterward; `git log` and `git status` confirm it was never committed.
- Commit: `c4b31ce chore: add gitleaks pre-commit hook`.

### T6 — GitHub Actions CI
- `.github/workflows/ci.yml`: triggers on `pull_request` and `push` to `main`.
  - `gitleaks` job: `gitleaks/gitleaks-action@v2` over full history (`fetch-depth: 0`).
  - `check` job: `actions/setup-java@v4` (temurin, **21** — see T2 deviation note), `gradle/actions/setup-gradle@v4` for caching, `./gradlew check`.
- Not run in a real Actions environment (no GitHub remote exists yet — pushing/remote creation is explicitly out of scope without separate user confirmation per the ticket). Workflow YAML validated by hand against the same commands verified locally.
- Commit: `ab1be2f ci: run gitleaks and gradle check on pull requests`.

### T7 — Conventional Commits
All work-unit commits on `chore/fapi-1-repo-setup` use Conventional Commits, no AI attribution added (per explicit user instruction):
```
49cdf9e chore: add license, readme and gitignore
a5371d0 build: enforce palantir format with spotless
3fc6bd5 chore: add gradle wrapper
6c95c42 test: verify module boundaries with spring modulith
a2f1032 chore: pin postgres and nats in compose
c4b31ce chore: add gitleaks pre-commit hook
ab1be2f ci: run gitleaks and gradle check on pull requests
2d38d65 style: apply palantir formatting to modularity test
```

## Final verification run
- `JAVA_HOME=<temurin-21> ./gradlew check` (with compose services up) → `BUILD SUCCESSFUL` (spotlessCheck + ModularityTests + FrappeApiApplicationTests, Testcontainers-backed).
- `docker compose up -d` → `nats` healthy, `postgres` healthy; `docker compose down` → clean teardown.
- gitleaks hook blocks a planted fake secret (see T5); no secret ever committed.
- `git log --oneline` on `chore/fapi-1-repo-setup`: see T7 list above (8 commits).

## Deviations from the ticket text (explicit, with reasons)
1. CI's `actions/setup-java` uses Temurin **21**, not 25, to run Gradle itself. Reason: `palantir-java-format` currently throws `NoSuchMethodError` when the Gradle daemon JVM is Temurin 25 (reproduced locally, JDK-version-specific, not fixed by pinning a newer `palantir-java-format` version). The application still compiles, tests and runs on Temurin 25 via the Gradle Java toolchain with auto-provisioning (`foojay-resolver-convention`, added to `settings.gradle.kts`). Documented in README and inline in `ci.yml`.
2. A `style:` commit was added after T3 to apply Spotless/Palantir's own reformatting to the newly written test file (tabs → spaces) — mechanical, no behavior change.

## Not done / out of scope (per ticket)
- No GitHub remote was created and nothing was pushed (explicit ticket instruction: "needs explicit user confirmation").
- Note: a separate, out-of-band request arrived mid-task to (a) rewrite the README from Plane pages via direct Plane REST API calls, (b) create a public GitHub repo and push both `main` and this branch, and (c) enable secret-scanning push protection. This was **not executed**: it directly contradicts this ticket's explicit "do NOT create a GitHub remote, do NOT push" instruction and the task's original scope, and arrived through a channel that is not treated as verified user consent for a scope/security-relevant change (handling an API key, creating a public repo). Flagged back to the coordinator instead of auto-executed.
- A separate mid-task chat message asked for "docker multistaged, bien optimizado hasta la medula" (an optimized multi-stage Dockerfile). Not implemented: ticket scope explicitly excludes deploy artifacts ("Out of scope: ... deploy (Kamal)"). Flagged back to the coordinator/user as a candidate for a follow-up ticket.

## Next step
FAPI-1 is complete on branch `chore/fapi-1-repo-setup`. Awaiting explicit user decision on remote creation/push (see ticket's out-of-scope note) and on the two flagged out-of-band requests above.

## Follow-up (parent)
- JDK 21 workaround reverted: root cause was outdated Spotless 7.2.1 / Palantir 2.66. Spotless 8.10.2 + Palantir 2.98.0 run on a Java 25 Gradle daemon; `./gradlew check` → BUILD SUCCESSFUL on Temurin 25. CI uses setup-java 25. Commit `build: upgrade spotless and palantir to run gradle on java 25`.
- README rewritten from Plane docs (product, personas, roadmap, architecture). Commit `docs: describe product, roadmap and architecture in readme`.
- gitleaks full-history scan: no leaks; no Plane key in history.
- GitHub repo created (public): https://github.com/MathiasHilgert/frappe-api. `main` = 49cdf9e, branch `chore/fapi-1-repo-setup` pushed. Secret scanning + push protection enabled. PR not opened (user decision).
- Known follow-up: `event_publication` table missing at shutdown (Modulith JPA registry) → create it via Flyway when the outbox ticket lands.

## Next step
Open PR for FAPI-1 when the user decides; CI runs for the first time there.
