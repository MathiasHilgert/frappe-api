# Troubleshooting

Start with `just doctor`: it names every missing prerequisite.

| Symptom | Cause | Fix |
| --- | --- | --- |
| `just dev` fails with "port is already allocated" | Another Postgres, Valkey, NATS or Mailpit is bound to 5432, 6379, 4222 or 8025 | Stop the other service, or change the left side of the port in `deploy/compose.yaml` locally |
| The API logs `Failed to connect to 127.0.0.1:5432` | The stack is not up | `just dev` (or `just up`) |
| `relation "identity.principals" does not exist` | Migrations not applied | `just migrate`; `just dev` runs it every time |
| No mail in Mailpit | The outbox delivers after commit, a second later; or `Notification:Smtp:Host` is blank in your environment | Wait and refresh http://localhost:8025; check `appsettings.Development.json` and your user-secrets |
| Startup fails: `'Notification:Resend:ApiKey' is required outside Development` | `ASPNETCORE_ENVIRONMENT` is not `Development` | Run with the `http` launch profile (`just dev`) |
| Startup fails: `... is the committed development key` | A non-Development environment uses the development seal key | Set a real `Messaging__Seal__Key` |
| Build fails in `Internal/Generated` after changing a handler (Release only) | The committed handler code is stale | `just codegen`, then commit it; Debug builds do not compile it |
| CI fails `codegen-check` | Same | `just codegen` and commit `src/Host/Frappe.Api/Internal/Generated` |
| Integration tests fail with Docker errors | Docker daemon not running, or Testcontainers cannot reach it | Start Docker; `docker info` must succeed |
| `just test-int Name` reports zero tests | The filter matches method names, not class names | Use part of the method name, e.g. `just test-int TheRightPassword` |
| `just seed` says the API is not running | `just dev` is not running in another terminal | Start it, wait for "Now listening", rerun `just seed` |
| `just seed` fails with 409 on registration | The seed email is registered with another password | Delete only that user from your local database, or change `scripts/dev/seed-owner.env` |
| Commit refused: "staged C# files are not formatted" | The pre-commit hook found formatting drift | `just fix`, stage, commit again |
| Commit refused: header format | Not a Conventional Commit | `type(scope): subject`, 100 characters at most |
| `GET /v1/stream` answers 403 | No business module grants membership yet; the stream is deny-by-default | Expected until a business module implements `IBusinessMembership` |
