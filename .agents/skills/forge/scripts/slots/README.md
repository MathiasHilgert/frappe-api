# Slots: one isolated local stack per session

`slot` gives each concurrent session its own copy of the stack (slots 1–3). All slots send telemetry to one shared
LGTM stack. Slots never share containers, volumes, ports or images. A fourth claim waits.

## Quick path

```bash
S=.agents/skills/forge/scripts/slots/slot
n=$($S claim --worktree "$PWD")    # prints 1..3; waits up to 30 min when all are taken
$S up --slot "$n"                  # starts shared LGTM if needed, builds, waits for /health/ready
# The user's own `frappe` stack holds the default LGTM ports? Move the shared stack instead:
#   FRAPPE_OBS_PORT_OFFSET=10000 $S up --slot "$n"   (Grafana 13000, Mimir 19009, Tempo 13200, OTLP 14317)
api=$($S url --slot "$n")          # e.g. http://localhost:8180
# ... test against $api ...
$S release --slot "$n"             # down -v, free the lock, delete generated files
```

`up` prints `key=url` lines on stdout: `api`, `grafana`, `mimir`, `tempo`, `loki`, `otlp_grpc`. Progress
goes to stderr. `ports` and `observability status` print the same shared URLs, or `observability=stopped`.

## Commands

| Command | Does |
|---------|------|
| `claim [--worktree P] [--wait S]` | Takes the first free slot and prints its number. Removes leftovers of a dead previous holder. Fails after `--wait` seconds (default 1800). |
| `env --slot N [--worktree P]` | Writes `run/s<N>/slot.env` and `run/s<N>/compose.override.yaml` (`up` runs it for you). |
| `up --slot N [--worktree P] [--timeout S]` | Builds and starts postgres, nats, valkey, migrator, api and worker, then waits for the Api (default 300 s). |
| `down --slot N [--volumes]` | Stops the slot's project. Keeps the claim. |
| `release --slot N` | Runs `down --volumes`, kills the holder and deletes `run/s<N>`. |
| `status` | Every slot: lock, holder PID and liveness, worktree, containers. Also shows the shared stack. |
| `gc` | Releases slots whose holder is dead or whose worktree is gone. Also cleans containers of unclaimed slots. |
| `ports --slot N` / `url --slot N` | Ports the slot publishes plus the shared URLs / the Api base URL. |
| `observability up\|down\|status` | The shared LGTM stack. |

`--worktree` defaults to the current directory for `claim`, and to the claimed worktree after that.

## What a slot gets

| Item | Slot `n` |
|------|----------|
| Compose project | `frappe-s<n>` |
| Image | `frappe-api:s<n>` (migrator, api, worker) |
| Host ports | compose defaults + `n × 100`: Api `8080+100n`, Postgres `5432+100n`, NATS `4222+100n`/`8222+100n`, Valkey `6379+100n` (a base is overridable per port with `FRAPPE_SLOT_BASE_<VAR>`) |
| Telemetry | OTLP to `frappe-obs-collector:4317` on network `frappe-observability`, tagged `deployment.environment=s<n>` |

Query a slot's telemetry with the label `deployment_environment="s<n>"` on `target_info` in Mimir, or with
`{resource.deployment.environment="s<n>"}` in Tempo.

## Shared observability

- Compose project `frappe-observability`, started from the main checkout's `deploy/compose.yaml`. It runs
  otel-collector, loki, tempo, mimir and grafana only.
- Its collector joins the external network `frappe-observability` under the alias `frappe-obs-collector`. `observability up`
  creates the network when it is missing.
- It uses the default ports: Grafana 3000, OTLP 4317/4318, Loki 3100, Tempo 3200, Mimir 9009.
  `FRAPPE_OBS_PORT_OFFSET=10000` moves them, for example to Grafana 13000.
- When another container already publishes one of those ports, `observability up` fails and names that container.
  The user's own `frappe` stack is one example. The script never stops other projects.

## Locks and holders

- Each slot has a lock at `~/.local/state/frappe/slots/<n>.lock`. `claim` takes it with `flock -n`, then starts a
  detached `setsid sleep infinity` that inherits the locked fd. That process is the holder: the lock lives exactly
  as long as it does.
- If the holder dies (kill, reboot), the kernel frees the lock. `gc` then removes the stale record and the slot's
  containers.
- `~/.local/state/frappe/slots/<n>.json` records `slot`, `worktree`, `holder_pid` and `claimed_at`.
- `release` kills `holder_pid` only after it checks, in `/proc/<pid>/fd`, that the process still holds the lock
  file. A recycled PID is never killed.
- `status` and `gc` probe the lock itself. They do not trust the JSON.

## Files

| Path | Content |
|------|---------|
| `run/s<n>/slot.env` | Project name, image tag, ports, OTel attributes. Loaded after the base env file. |
| `run/s<n>/compose.override.yaml` | Image, OTLP endpoint and network for migrator, api and worker. |
| `run/s<n>/base-env-path` | Base env file, picked in this order: the worktree's `deploy/.env`, then the main checkout's `deploy/.env`, then `deploy/.env.example` (with a warning). |
| `run/observability/` | Ports and override of the shared stack. |

The script never writes any `deploy/.env` or any tracked file. The generated values are exported into the compose
process, so variables exported in the caller's shell (`API_PORT`, ...) cannot move a slot.

## Limits

- `down`, `release` and `gc` work by project name, so they still work after the worktree is deleted.
- Images `frappe-api:s<n>` stay after `release`. The next `up` reuses their build cache.
- `observability down` keeps the LGTM volumes and the `frappe-observability` network.
