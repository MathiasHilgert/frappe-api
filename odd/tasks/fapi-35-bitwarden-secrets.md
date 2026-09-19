# FAPI-35 — platform: Manage secrets in Bitwarden Secrets Manager from day one

Plane: [FAPI-35](https://app.plane.so/nulled-software/browse/FAPI-35/) (module platform, size S, type chore, sensitive: secrets). Branch: `chore/fapi-35-bitwarden-secrets`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-35`.

## Objective
Secrets live in Bitwarden Secrets Manager (EU cloud). Developers run any command with a project's secrets injected through `scripts/with-secrets.sh`; the app keeps reading environment variables only and the `local` profile keeps working without Bitwarden.

## Scope
- `scripts/with-secrets.sh` (+ `scripts/bws.toml`, the committed non-secret `bws` profile for the EU server).
- `docs/secrets.md`: human setup (EU org, projects, read-only machine accounts), inventory, runbook (add, rotate, revoke, leak).
- README section; link from the `writing-code` skill.
- Out of scope: deploy wiring (FAPI-30), creating accounts or tokens (human prerequisite).

## Constraints
- No Bitwarden account exists yet: no credentials are used; only failure paths and the dry run are exercised.
- The script never prints or writes a secret value or the access token.
- The app reads only environment variables (12-factor).

## TDD
Strict TDD (project rule, AGENTS.md). Runner: `scripts/with-secrets.test.sh` (hermetic bash test with a fake `bws` on `PATH`), wired into `./gradlew check` as `scriptTests`. Gate for this ticket: `./gradlew spotlessApply check -x test` (brief), plus `shellcheck` and `gitleaks`.

## Tasks
- [x] T0 Verify `bws` (latest), EU targeting, `bws run` semantics, read-only machine accounts, Kamal `bitwarden-sm`; record findings
- [x] T1 `scripts/with-secrets.sh`: RED test, then script (help, missing `bws`, missing token, dry run, project by name, EU profile, no state file, argument quoting, exit code)
- [x] T2 `docs/secrets.md`, README section, `writing-code` link
- [x] T3 Gate: `shellcheck`, `gitleaks`, `./gradlew spotlessApply check -x test`; commit
- [x] T4 Review (Opus changes requested): CI runs the whole `./gradlew check`; reserved secret names refused; `BWS_UUIDS_AS_KEYNAMES` ignored; empty `--project` rejected; tests for `bws.toml` and newline arguments; docs minors
- [x] T5 (human + orchestrator) Live run against `frappe-dev`; `frappe-production` created, staging removed (reader tokens pending)

## Acceptance (from ticket)
- Developer token for frappe-dev: `scripts/with-secrets.sh ./gradlew bootRun` starts the app with the secrets; none written to disk or printed. (Real run: human, after the org exists.)
- No token: the script explains how to get one; the local profile path still works.
- gitleaks finds no secret; the inventory lists every secret the app reads.

## T0 findings (primary sources, 2026-09-19)
Sources: GitHub releases API of `bitwarden/sdk-sm`; `sdk-sm` source at tag `bws-v2.1.0` (`crates/bws/src/{cli,main,config,state}.rs`, `command/run.rs`); `bitwarden/sdk-internal` (`bitwarden-core` `secrets_manager/state.rs`, `client/client_settings.rs`); bitwarden.com/help pages `secrets-manager-cli`, `machine-accounts`, `access-tokens`, `server-geographies`, `secrets-manager-plans`; `basecamp/kamal` `lib/kamal/secrets/adapters/bitwarden_secrets_manager.rb` and kamal-site `docs/commands/secrets.md`.

- **Latest `bws`**: `bws-v2.1.0` (2026-05-21; dependency updates). `bws-v2.0.0` (2026-02-05) fixed a panic when a project has no secrets and added static musl Linux binaries. Install: GitHub release zip (checksums file published), `cargo install bws --locked`, or `curl https://bws.bitwarden.com/install | sh`.
- **EU server**: the SDK default is the US cloud (`api.bitwarden.com`, `identity.bitwarden.com`). The CLI docs configure the EU with `bws config server-base https://vault.bitwarden.eu`; with only `server_base` set, `bws` uses `<base>/api` and `<base>/identity`. Equivalent per run: `--server-url` / `BWS_SERVER_URL`. Caveat (source): a server URL builds a profile *without* `state_opt_out`, so the state file would be written; a named profile from a config file (`BWS_CONFIG_FILE` + `BWS_PROFILE`) can set both `server_base` and `state_opt_out`.
- **State file**: unless opted out, `bws` writes `~/.config/bws/state/<access-token-id>`: the session bearer token and the organization encryption key, encrypted with the access token's key (not secret values). The docs note a stored session keeps working after the access token is revoked until it expires. Decision: opt out (`state_opt_out = "true"` in the committed profile), so nothing is written; cost: one authentication per run (rate limits only matter for tight loops).
- **`bws run`**: lists secrets (of `--project-id <uuid>` or all the token can read), fails on duplicate keys, sets them as environment variables of the child named by secret key, and runs `<shell> -c "<args joined by spaces>"` (default `sh`; `--shell` selects another). The child inherits the parent environment minus `BWS_ACCESS_TOKEN` (removed). The exit code of the child is propagated. Nothing is written to disk by `run` itself. Consequence: arguments are re-parsed by the shell, so the wrapper quotes each argument (`printf %q`) and passes `--shell bash`.
- **Project selection**: `--project-id` takes a UUID only. The wrapper resolves the project name with `bws project list --output tsv` (columns `ID`, `Name`, `Creation Date`); a machine account lists the projects it can access (Kamal's adapter relies on the same call to check login).
- **Machine accounts**: per project permission `Can read` (retrieve secrets) or `Can read, write`. Access tokens belong to one machine account, are shown once, never stored by Bitwarden, can have an expiry (default never) and can be revoked; a session already issued may keep working for up to one hour after revocation.
- **Plan**: the Free Secrets Manager plan allows 2 users, 3 projects and 3 machine accounts. Decision (human, after T5): no staging environment; two projects `frappe-dev`, `frappe-production` with one reader each.
- **Kamal (FAPI-30)**: Kamal v2.12.0; adapter `bitwarden-sm`, no account needed; `kamal secrets fetch --adapter bitwarden-sm <project-uuid>/all` runs `bws secret list <project-uuid>` (also `all`, or single secret UUIDs), then `kamal secrets extract NAME ...`. It shells out to `bws` and so reads `BWS_ACCESS_TOKEN` and `BWS_SERVER_URL` (EU) or `BWS_CONFIG_FILE`/`BWS_PROFILE` from the environment.

## Decisions
- The EU server and state opt-out live in a committed, secret-free `bws` config (`scripts/bws.toml`, profile `frappe-eu`); the script sets `BWS_CONFIG_FILE`/`BWS_PROFILE` and unsets `BWS_SERVER_URL` (which would override the profile and re-enable the state file).
- Default project `frappe-dev`; `--project NAME` or `FRAPPE_SECRETS_PROJECT` selects another.
- `--dry-run` never contacts Bitwarden: it reports whether `bws` and the token are present (never the value) and prints the planned invocation.
- Exit codes: 2 usage, 1 missing prerequisite or lookup failure, otherwise the command's own exit code.
- Script test wired into `./gradlew check` (`scriptTests`), so the local gate covers it; CI runs the same gate (T4).
- Reserved names (review): the script lists the project's secret names (`bws secret list`, values held in memory only) and refuses names that steer the shell, linker, JVM, Gradle, Spring or bws (deny-list of exact names and prefixes `LD_ DYLD_ BASH_FUNC_ BWS_ JAVA_ JDK_ _JAVA_ GRADLE_ SPRING_ GIT_`). Naming convention documented as `FRAPPE_*` or a known third-party key; write access to a project is code execution for its consumers. Residual risk: the check and `bws run` list the secrets separately, so a secret added in between is not checked (writers are trusted people; documented).

## Progress / evidence
- T0: done (findings above).
- T1 RED: `scripts/with-secrets.test.sh` (12 cases) before the script existed: `with-secrets.sh: 42 failure(s) in 12 tests` (every case exit 127, `No such file or directory`).
- T1 GREEN: `with-secrets.sh: 12 tests passed` (help; no command; unknown option; missing bws; missing token; dry run without bws/token; dry run with token, no leak, no Bitwarden call; run via EU profile with `BWS_SERVER_URL` removed, secret injected, token absent in child; arguments with spaces/quotes/`$`; project by option and by env; unreadable project; exit code propagated). REFACTOR: shellcheck 0.11.0 clean (SC2155 split, two intentional SC2016 annotated); wired into `./gradlew check` as `scriptTests` (`./gradlew scriptTests`: BUILD SUCCESSFUL, no deprecation).

- T1 commit: `a1775a8` (rebased onto `main` d1858e0) chore(platform): run commands with bitwarden secrets injected.
- T2: `docs/secrets.md` (layout, human setup with verification steps, script behaviour, inventory: app secrets `FRAPPE_APP_PASSWORD`, `FRAPPE_OWNER_PASSWORD`, `FRAPPE_SECRET_PEPPER`, `FRAPPE_VALKEY_URL`, `FRAPPE_NATS_URL` (pepper and Valkey URL added after rebasing onto `main` with FAPI-16 and FAPI-15); infrastructure/tooling `POSTGRES_PASSWORD`, `BWS_ACCESS_TOKEN`, `PLANE_API_KEY`, `GITHUB_TOKEN`; non-secret configuration listed; runbook add/rotate/revoke/leak). README "Secrets" section; `writing-code` hard rule + decision-gate row linking `docs/secrets.md`. Inventory source: `rg '\$\{' src/main/resources`, `@ConfigurationProperties` (`frappe.nats.*`, `frappe.outbox.recovery.*`), `compose.yaml`, `docker/postgres/initdb`, `.github/workflows`.
- T3: dry run in the real environment (no `bws`, no token): exit 0, reports both missing, EU server, project `frappe-dev`. Real run without `bws`: exit 1 with install help and the local-profile hint. `shellcheck -x scripts/*.sh`: clean. `gitleaks dir .` and `gitleaks git .`: no leaks found. `./gradlew spotlessApply check -x test`: BUILD SUCCESSFUL (includes `scriptTests`: 12 passed). The full `./gradlew check` (Testcontainers suite) was not run for this change, which touches no Java.

- T4 RED: 4 new cases plus assertions; `with-secrets.sh: 29 failure(s) in 16 tests` (reserved names not refused and no `secret list` call; empty `--project` accepted (exit 1 instead of 2); `BWS_UUIDS_AS_KEYNAMES=true` reached bws). The newline-argument and `bws.toml` cases passed at once: they pin behaviour that already existed.
- T4 GREEN: `with-secrets.sh: 16 tests passed`; shellcheck clean. The UUID row filter avoids awk interval expressions (older mawk).
- T4 CI: `ci.yml` quality gate steps: spotlessCheck, javadoc, scriptTests, `test --tests ModularityTests`, test, then `./gradlew check` (so every task `check` runs also runs in CI; `./gradlew check --dry-run`: compileJava, processResources, classes, javadoc, scriptTests, spotlessJava(Check), spotlessKotlinGradle(Check), spotlessCheck, compileTestJava, processTestResources, testClasses, test, check). JUnit report path and job summary unchanged; README CI story and diagram updated.
- T4 docs: `compgen -e | sort` (both places), write access = code execution + naming rule + reserved names, developer invitation as User with Can read, write on `frappe-dev` only (Owners/Admins see every project), probe cleanup step, leak runbook `.gitleaksignore` by fingerprint or history rewrite (fingerprint command verified with gitleaks 8.30.1 on a scratch repo).

## T5 live run (orchestrator with the human's token; recorded as a comment on FAPI-35, no values)
- `bws` 2.1.0 installed, release checksum verified; the committed EU profile (`scripts/bws.toml`) works.
- Projects `frappe-dev` and `frappe-staging` existed, each with `POSTGRES_PASSWORD`, `FRAPPE_APP_PASSWORD`, `FRAPPE_OWNER_PASSWORD`, `FRAPPE_SECRET_PEPPER` (generated values).
- `scripts/with-secrets.sh bash -c 'compgen -e ...'` injected all four names; `BWS_ACCESS_TOKEN` absent in the child; no `~/.config/bws/state` afterwards.
- `frappe-production` could not be created at first: the Free plan allows 3 projects and a third, pre-existing project occupied the slot.
- Human decision: there is no staging environment, only dev and production. The orchestrator deleted `frappe-staging` (and its secrets) and created `frappe-production` with generated `POSTGRES_PASSWORD`, `FRAPPE_APP_PASSWORD`, `FRAPPE_OWNER_PASSWORD`, `FRAPPE_SECRET_PEPPER`. Docs, README and script tests no longer mention staging (tests use `frappe-production` as the second project and `frappe-archive` as the unreadable one).
- **Finding**: the token used could create projects and secrets, so it was not a read-only machine-account token (step 9.5 of the setup would fail). The invariant "read-only machine accounts per environment" is not yet met in the live organization.
- Docs follow-up: the dev database passwords differ from the local defaults and apply only on a new volume (the roles script keeps existing passwords), so `docs/secrets.md` now says to start compose through `scripts/with-secrets.sh` on a fresh volume (or set the passwords with `\password`).

## Pending
- **Human**: create the two read-only machine accounts `frappe-dev-reader` and `frappe-production-reader` (**Can read** on their own project only) and issue their tokens; repeat setup steps 9.5–9.6 with the dev reader token and record the result in FAPI-35.
- **Human**: revoke the setup token that could create projects and secrets.
- Deploy wiring and the same reserved-name rule for Kamal: FAPI-30.

## Engram mirror
Pending: no Engram tools in the implementing agent; the orchestrator mirrors `odd/fapi-35-bitwarden-secrets/tasks`.

## Next step
Human: read-only reader tokens and revoking the setup token (Pending); then merge PR #19.
