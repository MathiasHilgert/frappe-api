# Bruno collection

The requests of the Api, as a [Bruno](https://www.usebruno.com/) collection in the
[OpenCollection YAML](https://docs.usebruno.com/opencollection-yaml/overview) format. Each request asserts its status
and response shape; error requests assert the Problem Details category (`type`) and error `code`. The platform's own
requests are here; a module adds a folder of its own.

| Folder | Covers |
|--------|--------|
| Health | `/health/live`, `/health/ready`, `/health/startup` |
| HTTP Conventions | The 404 Problem Details of an unknown route |
| OpenAPI | `/openapi/v1.json` |
| Notifications | `POST /v1/notifications/sends`, Development only: its requests skip themselves against `Stack`, which runs as Production; `POST /v1/notifications/webhooks/resend`, signed by its own script |

The 503 body of a failing probe is pinned by the Api's integration tests, not by this collection, which only sees a healthy stack.

## Open it

Bruno reads YAML collections since 3.0.0; this one is written for Bruno 4.3 or later. CI runs it with Bruno CLI 4.3.0
against the compose stack (the `Bruno collection passes` check), so a failing request is a real regression.
URLs are written unencoded: every request has `encodeUrl: true`, so Bruno encodes them once. In Bruno, choose
**Open Collection** and pick this folder (`tools/bruno`, the one with `opencollection.yml`). Then select an environment.

## Environments

Both define the same variables, so a request works the same against either.

| Environment | `baseUrl` | Needs running |
|-------------|-----------|---------------|
| Local | `http://localhost:5080` | `just infra up`, `just db migrate` and `just run api` |
| Stack | `http://localhost:8080` | `just stack up` (if you moved `API_PORT` in `deploy/.env`, change `baseUrl`) |

The variable `language` (`es-AR`) is sent as `Accept-Language` by every request. `webhookSecret` is the local-only placeholder of
`deploy/.env.example`, which the webhook requests sign with; the collection holds no real secret, and an environment that points at a
deploy with its own secret sets its own.

## Run it from the command line

From this folder, with the Api running:

```bash
npx -y @usebruno/cli@4.3.0 run --env Local
```

It runs every folder in order and exits non-zero when a request or an assertion fails. Pass `--env Stack` for the stack,
or a folder (`run health --env Local`) to run only that one.
