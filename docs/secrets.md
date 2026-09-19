# Secrets

Secrets live in [Bitwarden Secrets Manager](https://bitwarden.com/products/secrets-manager/) on the EU cloud (`vault.bitwarden.eu`), never in the repository. The application reads **environment variables only**; how they get there (Bitwarden locally and in CI, Kamal on deploy) can change without code changes.

- **Local work** without real keys needs nothing: `./gradlew bootRun` uses the `local` profile defaults.
- **Local work with real keys**: `scripts/with-secrets.sh ./gradlew bootRun` injects the `frappe-dev` project into that one process.
- **Deploys** (FAPI-30) read `frappe-staging` / `frappe-production` through Kamal's `bitwarden-sm` adapter.

## Layout

| Bitwarden object | Name | Access |
| --- | --- | --- |
| Organization | Frappé (EU cloud, Secrets Manager Free plan: 2 users, 3 projects, 3 machine accounts) | Owner: Mathias Hilgert |
| Project | `frappe-dev`, `frappe-staging`, `frappe-production` | People: developers read/write `frappe-dev`; only the owner edits staging and production |
| Machine account | `frappe-dev-reader`, `frappe-staging-reader`, `frappe-production-reader` | **Can read** on its own project only |
| Access token | one per holder, named after it (`dev-<person>-<device>`, `ci-staging`, `deploy-production`) | Expires after 90 days |

One read-only machine account per environment means a leaked dev token can read dev secrets and nothing else: it cannot write, and it cannot see staging or production.

**Write access to a project is code execution for everyone who consumes it.** Every secret becomes an environment variable of the command that runs with it (`bootRun` on a laptop, a deploy), and variables such as `LD_PRELOAD`, `BASH_ENV` or `JAVA_TOOL_OPTIONS` make that command run code of the writer's choice. Grant write access only to people you would give that power, and keep secret names to `FRAPPE_*` or a known third-party key name (`DEEPL_API_KEY`, `RESEND_API_KEY`, `POSTGRES_PASSWORD`). `scripts/with-secrets.sh` lists the project's secret names first and refuses to run when one is reserved: `PATH`, `HOME`, `IFS`, `BASH_ENV`, `ENV`, `CLASSPATH`, `NODE_OPTIONS`, `PYTHONPATH` and similar, or any name starting with `LD_`, `DYLD_`, `BASH_FUNC_`, `BWS_`, `JAVA_`, `JDK_`, `_JAVA_`, `GRADLE_`, `SPRING_` or `GIT_`. Deploys (FAPI-30) need the same rule.

## Human setup (once)

1. Go to <https://vault.bitwarden.eu> (or choose **bitwarden.eu** in the "Logging in on" / server dropdown of the login or registration screen) and create the owner account there. Regions are separate: an account or organization exists only in the region where it was created. Turn on two-step login.
2. **New organization** → Free plan → name `Frappé`. In the organization, subscribe to **Secrets Manager** (Free).
3. Switch to **Secrets Manager** (product switcher). **New → Project** three times: `frappe-dev`, `frappe-staging`, `frappe-production`.
4. **New → Machine account** three times: `frappe-dev-reader`, `frappe-staging-reader`, `frappe-production-reader`. In each one, **Projects** tab: add only the matching project with permission **Can read**.
5. **Invite developers** (Admin console → Members → Invite) as role **User**, with Secrets Manager access, and grant them **Can read, write** on `frappe-dev` only (project → **People**). Owners and Admins see every project, staging and production included, so keep those roles to the secrets owner.
6. In each machine account, **Access tokens → Create access token**: name it after the holder, expiry **90 days**. The token is shown once and never stored by Bitwarden: put it straight into the holder's password manager (the owner's Bitwarden vault) or the CI/deploy secret store. Never in a file inside the repository, a ticket or a chat.
7. **Add the secrets** listed in the inventory below to their projects. The secret **name is the environment variable** (`FRAPPE_APP_PASSWORD`), unique within a project (`bws run` refuses duplicates). Put the purpose in the note.
8. **Install `bws`** 2.1.0 or later: a release from <https://github.com/bitwarden/sdk-sm/releases> (verify the published SHA-256 checksum) or `cargo install bws --locked`. `bws --version`.
9. **Verify** with the dev token in your shell only (`export BWS_ACCESS_TOKEN=...`, e.g. read from your password manager's CLI; not in `~/.bashrc`):
   1. `scripts/with-secrets.sh --dry-run ./gradlew bootRun` → `bws` found, token set, project `frappe-dev`.
   2. `scripts/with-secrets.sh bash -c 'compgen -e | sort'` → the secret **names** of `frappe-dev` appear among the variable names (names only; never print values).
   3. `scripts/with-secrets.sh ./gradlew --no-daemon bootRun` → the app starts with those values.
   4. `test ! -e ~/.config/bws/state && echo "no state file"` → nothing was written.
   5. Read-only and isolation: `BWS_CONFIG_FILE=scripts/bws.toml BWS_PROFILE=frappe-eu bws project list --output tsv` shows only `frappe-dev`, and `BWS_CONFIG_FILE=scripts/bws.toml BWS_PROFILE=frappe-eu bws secret create PROBE x <frappe-dev id>` is refused.
   6. Clean up: if the probe was **not** refused, the machine account can write. Delete the `PROBE` secret in the web app, set the machine account's permission back to **Can read**, and repeat step 5.
   7. Record the run (date, `bws --version`, results of 1–6, no values) in FAPI-35.

## How `scripts/with-secrets.sh` works

```bash
scripts/with-secrets.sh ./gradlew bootRun                       # frappe-dev
scripts/with-secrets.sh --project frappe-staging ./some-check   # another project the token can read
scripts/with-secrets.sh --dry-run ./gradlew bootRun              # what would run; contacts nothing
```

- Needs `bws` and `BWS_ACCESS_TOKEN`; without either it explains how to get them and exits 1. The local profile path (`./gradlew bootRun`) keeps working without both.
- Talks to the EU cloud through the committed, secret-free profile `scripts/bws.toml` (`BWS_CONFIG_FILE` + `BWS_PROFILE=frappe-eu`), which also opts out of the `bws` session state file. `BWS_SERVER_URL` is ignored on purpose: it would override the profile and turn the state file back on.
- Resolves the project name to its id (`bws project list`), then `bws run --project-id <id> --shell bash -- <command>`. The values exist only in the environment of that command; the token itself is removed from the command's environment by `bws`. Nothing is printed or written to disk.
- Before running, lists the project's secret names (`bws secret list`; values are held in memory only and dropped at once) and refuses reserved names (see "Layout"); `bws run` lists them again, so a secret added in that moment is not checked, one more reason to limit write access. `BWS_UUIDS_AS_KEYNAMES` is ignored: variables are always named after the secrets.
- `FRAPPE_SECRETS_PROJECT` changes the default project; `--project` must not be empty. The command's exit code is returned.
- Prefer `./gradlew --no-daemon ...` (or `./gradlew --stop` afterwards): a Gradle daemon started inside `with-secrets.sh` keeps those variables in memory until it stops.
- Tests: `scripts/with-secrets.test.sh` (fake `bws`, no network), part of `./gradlew check`.

## Inventory

Values never appear here or anywhere in the repository. Owner: the person who rotates the secret and answers for it. Rotation: at the latest after the period, and at once when a holder leaves or a leak is suspected.

### Secrets the application reads

| Name | Purpose | Owner | Projects | Rotation |
| --- | --- | --- | --- | --- |
| `FRAPPE_APP_PASSWORD` | Password of the runtime database role `frappe_app` (DML only); `spring.datasource.password` | Mathias Hilgert | `frappe-staging`, `frappe-production` (dev: local default `frappe_app`) | 90 days |
| `FRAPPE_OWNER_PASSWORD` | Password of the migration role `frappe_owner` (owns the schemas, runs Flyway); `spring.flyway.password` | Mathias Hilgert | `frappe-staging`, `frappe-production` (dev: local default `frappe_owner`) | 90 days |
| `FRAPPE_SECRET_PEPPER` | Server-side HMAC pepper for the Argon2id hashes of one-time codes; never reaches Valkey. At least 32 random characters (`openssl rand -base64 48`), different per environment; `frappe.secrets.pepper` | Mathias Hilgert | `frappe-staging`, `frappe-production` (dev: local default, not a secret) | 180 days; rotating only invalidates outstanding one-time codes |
| `FRAPPE_VALKEY_URL` | Valkey URL with its credentials (`rediss://user:password@host:6379`); `spring.data.redis.url` | Mathias Hilgert | `frappe-staging`, `frappe-production` (dev: local default `redis://localhost:6379`) | 90 days (the password in it) |
| `FRAPPE_NATS_URL` | NATS server URL; a secret as soon as it carries credentials (`nats://user:password@host:4222`); `frappe.nats.url` | Mathias Hilgert | `frappe-staging`, `frappe-production` (dev: default `nats://localhost:4222`) | 90 days when it carries credentials |

### Secrets of the infrastructure (not read by the application)

| Name | Purpose | Owner | Where | Rotation |
| --- | --- | --- | --- | --- |
| `POSTGRES_PASSWORD` | Bootstrap superuser of the Postgres container (compose, Kamal accessory) | Mathias Hilgert | `frappe-staging`, `frappe-production` (dev: compose default) | 90 days |
| `BWS_ACCESS_TOKEN` | Access token of one read-only machine account; the only secret a holder keeps outside Bitwarden Secrets Manager | Holder of the token | Holder's password manager, CI or deploy secret store | 90-day expiry set at creation |
| `PLANE_API_KEY` | Personal Plane key for the ticket tooling (`plane.sh`) | Each developer | Personal password manager, never a project | 90 days |
| `GITHUB_TOKEN` | Issued by GitHub Actions for each workflow run | GitHub | Automatic | Per run |

### Configuration, not secrets

Read from the environment too, but safe to show: `FRAPPE_DB_URL` (a secret only if it embeds credentials; keep them in the password variables), `FRAPPE_APP_USER`, `FRAPPE_OWNER_USER`, `FRAPPE_TRUSTED_PROXIES`, `FRAPPE_TRACING_SAMPLING_PROBABILITY`, the OTLP endpoints (see README, "Observability"), `SPRING_PROFILES_ACTIVE`, the other `FRAPPE_NATS_*` and `FRAPPE_OUTBOX_RECOVERY_*` settings, and the local compose ports (`FRAPPE_*_PORT`). They belong in the deploy configuration (FAPI-30).

## Runbook

### Add a secret

1. In the same PR as the code that reads it: read it as `${NAME}` in `application.properties` with **no default** (startup must fail without it), a harmless default in `application-local.properties` if local runs need one, and a row in the inventory above.
2. Create it in every project that needs it (**New → Secret**, name = variable, note = purpose). Developers never need production values; `frappe-dev` gets dev or sandbox keys (e.g. DeepL, Resend test keys).
3. Check: `scripts/with-secrets.sh bash -c 'compgen -e | sort'` lists the name.

### Rotate a secret

1. Create the new value at the provider (for database roles: `ALTER ROLE frappe_app PASSWORD '...'` as the owner, run through a password prompt, not a shell history).
2. Update the value in Bitwarden (edit the secret; same name).
3. Restart or redeploy the consumers (FAPI-30: `kamal deploy` reads the new value); verify health.
4. Invalidate the old value at the provider, where it can stay valid in parallel (API keys).

Rotate a **machine-account token** by creating a new token for the same holder, swapping it in the holder's store, then revoking the old one.

### Revoke access

- A person or device: machine account → **Access tokens** → revoke that holder's token. A session issued before can keep reading for up to one hour; when that matters, also rotate what the token could read.
- A developer leaving: revoke their tokens, remove them from the organization, rotate everything in the projects they could read or edit.

### After a leak

A token or value that reached a place it should not (commit, log, ticket, chat, screenshot, shared terminal) is leaked, even if deleted a minute later.

1. **Contain**: leaked token → revoke it at once. Leaked value → rotate it at once (above). Treat every secret a leaked token could read as leaked and rotate those too.
2. **Clean up**: remove it from the place it leaked to. In git, rotating comes first: this repository is public, and history rewrites do not recall clones. Then either rewrite the history (only when the value must not stay readable, e.g. personal data), or keep it and add the finding's fingerprint (from `gitleaks git --redact --report-format json --report-path -`, field `Fingerprint`; verified with gitleaks 8.30) to `.gitleaksignore` with a comment naming the rotation, so the CI scan stays green without ignoring anything else. `gitleaks git` confirms the working tree and history are clean afterwards.
3. **Investigate**: check the provider's logs (database connections, API usage) for use since the leak, and Bitwarden's event logs where the plan has them.
4. **Record**: a Plane ticket with what leaked, when, how, what was rotated, and what prevents a repeat. No values in it.
