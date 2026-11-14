# Bruno collection

Every endpoint of the Api, with its main error cases, as a [Bruno](https://www.usebruno.com/) collection in the
[OpenCollection YAML](https://docs.usebruno.com/opencollection-yaml/overview) format. Each request asserts its status
and response shape; error requests assert the Problem Details category (`type`) and error `code`.

| Folder | Covers |
|--------|--------|
| Health | `/health/live`, `/health/ready`, `/health/startup` |
| Geo | Every read endpoint of `/v1/geo`: catalogue and territory |
| HTTP Conventions | The 404 Problem Details of an unknown route |
| OpenAPI | `/openapi/v1.json` |

## Open it

Bruno reads YAML collections since 3.0.0; this one is written for Bruno 4.3 or later. Its files are parsed by Bruno CLI
4.3.0, but the collection has not yet been run end to end against a live Api, so a failing request may still be the
collection's fault. URLs are written unencoded (`name=Bruno check`): every request has `encodeUrl: true`, so Bruno
encodes them once (a hand-written `%20` would be sent as `%2520`). In Bruno, choose **Open Collection** and pick this folder
(`tools/bruno`, the one with `opencollection.yml`). Then select an environment in the top right corner.

## Environments

Both define the same variables, so a request works the same against either.

| Environment | `baseUrl` | Needs running |
|-------------|-----------|---------------|
| Local | `http://localhost:5080` | `just infra up`, `just db migrate` and `just run api` |
| Stack | `http://localhost:8080` | `just stack up` (if you moved `API_PORT` in `deploy/.env`, change `baseUrl`) |

The other variable is `language` (`es-AR`), sent as `Accept-Language` by every request. The Api is anonymous, so
there are no secrets. The Geo folders need the data the Worker syncs (`just run worker`, or the stack): it runs every
source once when it starts.

## Run it from the command line

The [Bruno CLI](https://docs.usebruno.com/bru-cli/overview) runs YAML collections. From this folder, with the Api
running:

```bash
npx -y @usebruno/cli@4.3.0 run --env Local
```

It runs every folder in order and exits non-zero when a request, test or assertion fails. Pass `--env Stack` for
the stack, or a folder (`run health --env Local`) to run only that one.
