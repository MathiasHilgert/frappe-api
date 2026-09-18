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
- [ ] T1 Repo hygiene: LICENSE (All rights reserved), README, .gitignore (exclude `.atl/`)
- [ ] T2 Spotless + Palantir wired into `check`; sources formatted
- [ ] T3 Modulith verification test + documentation test passing
- [ ] T4 compose.yaml with pinned Postgres and NATS JetStream; app boots against it
- [ ] T5 gitleaks pre-commit hook (versioned hook dir) + config
- [ ] T6 GitHub Actions: gitleaks + `./gradlew check` with Gradle cache
- [ ] T7 First commit(s) with Conventional Commits

## Checks
`./gradlew check`, `docker compose up -d` + app boot, gitleaks blocks a planted fake secret.

## Progress / evidence
_Pending._

## Next step
T1.
