# Development

## First time

```sh
just setup    # dotnet tools, packages, git hooks
just doctor   # lists every missing prerequisite (SDK from global.json, Docker, compose, just, git, curl, jq, hooks)
```

The devcontainer (`.devcontainer/devcontainer.json`) installs the SDK, Docker-in-Docker, just and jq, then runs both.

## Daily loop

```sh
just dev
```

`just dev` starts Postgres, NATS, Valkey, Mailpit and Grafana LGTM (`docker compose up -d --wait --no-recreate`: running containers are reused as they are, never recreated), applies every
module's migrations (`just migrate`, a no-op when current) and runs the API under `dotnet watch` with the `http`
profile. It never stops, resets or deletes anything, so rerun it whenever you like; Ctrl+C stops the API only, the
containers keep their data. `just down` stops the containers without deleting volumes.

Editing a Wolverine handler needs no extra step: Debug builds leave the committed generated code out and
Development generates handlers in memory at startup (`TypeLoadMode.Dynamic`). Before pushing a handler change, run
`just codegen` and commit `src/Host/Frappe.Api/Internal/Generated`; CI (`just codegen-check`) and the integration
tests load only that committed code, as production does.

## A user to sign in with

```sh
just seed
```

With `just dev` running, `just seed` registers the owner described in `scripts/dev/seed-owner.env` through the
public API, reads the verification code from Mailpit and verifies it. It is idempotent: an existing verified owner is
reused, a pending one is re-sent a code and verified. The credentials file is for local stacks only; the app never
reads it and no other environment has that user.

## Mail

Development never sends real mail, even when your user-secrets hold a Resend key. With
`Notification:Smtp:Host` set (appsettings.Development.json points it at `localhost:1025`), every mail goes to
Mailpit: read verification and reset codes at http://localhost:8025. With it blank, mail is only logged, masked
(recipient domain and subject with digits replaced, never the body).

## Calling the API

- Scalar, over the generated OpenAPI document: http://localhost:5131/scalar/v1 (Development only).
- `.http` files for the main flows in `src/Host/Frappe.Api/http/`: sign-up, sessions, account security and password
  reset. They work in Rider, Visual Studio and the VS Code REST Client with the `dev` environment from
  `http-client.env.json` (the seeded owner).

## Tests

| Command | Runs |
| --- | --- |
| `just test-unit [name]` | Unit tests (no Docker) |
| `just test-arch [name]` | Architecture tests |
| `just test-int [name]` | Integration tests (Docker, Testcontainers) |
| `just test [fast\|integration\|all]` | Whole suites, in parallel |

`name` filters by test method name, wildcards around it: `just test-int Login` runs every integration test whose
method name contains `Login`. Every recipe builds once in Release first.

## Git hooks

`just setup` points `core.hooksPath` at `scripts/git/hooks`:

- `pre-commit` runs `dotnet format --verify-no-changes` on the staged C# files only; fix with `just fix`.
- `commit-msg` checks the Conventional Commit header (100 characters at most) and refuses AI attribution. CI runs
  the full commitlint (`just commits`) as the authority.
