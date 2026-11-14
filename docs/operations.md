# Operations

How Frappé runs: what it reports, how it is probed, and the settings an environment can tune.

| Topic | Summary |
|-------|---------|
| [Telemetry](#telemetry) | Traces, metrics and logs over OTLP into Tempo, Mimir and Loki, linked in Grafana |
| [Health](#health) | Liveness, readiness and startup probes for every host |
| [Message retention](#message-retention) | The Worker purges each module's old outbox and inbox rows daily |
| [Relay and consumer tunables](#relay-and-consumer-tunables) | Batch sizes, timeouts and backoff of the event pipeline |
| [Runtime settings](#runtime-settings) | Shared infrastructure and reverse proxies |

Every setting is bound to an options type and validated when the host starts, before it serves: a missing or invalid
one stops the process with a message naming its key.

## Telemetry

Every host exports traces, metrics and logs over OTLP to the collector, which feeds Tempo, Mimir and Loki. One trace
follows a scheduled job from Quartz through its command to the outbox and JetStream, where other modules pick up its
integration events. In Grafana, open Explore: a trace links to its logs and metrics, and a log line links to its trace.

Modules only declare business metrics on a `Frappe.<Module>` meter (for example `frappe.<module>.<thing>.changed`); the
platform exports them with everything else. Sampling keeps every trace unless
`OTEL_TRACES_SAMPLER` says otherwise.

## Health

Each process exposes the three Kubernetes probes. Liveness checks only the process, so a dependency outage never
restarts it. Readiness is the sum of its critical dependencies (Postgres and NATS): one down takes the process out of
traffic until it answers again. Valkey is optional: a Valkey outage reports `Degraded` (still 200) and reads fall back
to the database. Startup says the host finished starting.

- Api: `GET /health/live`, `/health/ready` and `/health/startup`, with 200 or 503, the status and each check by name.
- Worker (no HTTP): TCP ports 8081 (live), 8082 (ready) and 8083 (startup), open only while that probe is healthy;
  move them with `Health__Probes__LivePort`, `ReadyPort` and `StartupPort`.
- Containers check themselves with `<host binary> --probe <http or tcp target>`, because the image has no curl.

`just stack health` prints the readiness of both hosts. A capability declares what it depends on where it registers
it: `services.AddCriticalDependency("name", check)` (or `AddOptionalDependency` for one that only degrades
readiness). A new module brings its own checks with it.

## Message retention

Each module that registers message retention has the Worker purge its `outbox_messages` and `inbox_messages` once a day, at 03:30 by default
(`Messaging__Retention__Cron`, a Quartz cron, seconds first: `0 30 3 * * ?`). Published outbox rows go after
`Messaging__Retention__OutboxDays` (default 7) days from their publication; unpublished rows, parked ones included,
are never purged. Inbox records go after `Messaging__Retention__InboxDays` (default 14) days. Consumers deliver from
the start of their stream, which keeps 7 days of messages, so an inbox retention shorter than that would let a replay
run a handler twice: the Worker refuses to start with it, naming the key. Neither retention may exceed 3650 days.
Rows are deleted in batches of 1000 (`Messaging__Retention__BatchSize`), each batch its own statement; every run logs
the rows purged per table and counts them in `frappe.messaging.purged` (meter `Frappe.Messaging`, labelled by
`frappe.module` and `frappe.messaging.table`).

## Relay and consumer tunables

The outbox relay and the JetStream consumers can be tuned per environment; the defaults are the values below.

| Key | Default | Meaning |
|---|---|---|
| `Messaging__Relay__BatchSize` | `50` | Most messages one relay pass takes. |
| `Messaging__Relay__PollInterval` | `00:00:01` | Pause after a pass that did not fill a batch. |
| `Messaging__Relay__PublishTimeout` | `00:00:05` | Upper bound for one publish. |
| `Messaging__Relay__MaxPassDuration` | `00:00:10` | Upper bound for a whole pass. |
| `Messaging__Relay__LeaseDuration` | `00:00:30` | How long a relay owns the rows it claimed; at least `MaxPassDuration + PublishTimeout + 5 s`. |
| `Messaging__Relay__MaxAttempts` | `10` | Rejected publishes before a message is parked. |
| `Messaging__Relay__RetryBackoff__0`, `__1`, ... | `00:00:01`, `00:00:05`, `00:00:30`, `00:02:00`, `00:10:00` | Wait after each failed publish; the last value repeats. A configured list replaces the default whole. |
| `Messaging__Consumers__MaxDeliveries` | `6` | Handler failures before a message is dead-lettered. |
| `Messaging__Consumers__Backoff__0`, `__1`, ... | `00:00:01`, `00:00:05`, `00:00:30`, `00:02:00`, `00:10:00` | Wait after each handler failure; the last value repeats. A configured list replaces the default whole. |
| `Messaging__Consumers__AckWait` | `00:00:30` | How long JetStream waits for an ack; at least 1 s. |
| `Messaging__Consumers__PrefetchCount` | `1` | Messages pulled ahead of processing. |
| `Messaging__Consumers__MaxHandlerDuration` | `00:05:00` | Longest a handler is kept alive; at least `AckWait`. |

## Runtime settings

- **Shared infrastructure.** Two optional settings exist for sharing one NATS or one Valkey between environments (the
  tests use them to run in parallel): `Messaging__Namespace` prefixes every JetStream stream and subject (lowercase;
  the CloudEvents type stays unprefixed), and `Caching__InstanceName` (default `frappe`) prefixes cache keys and the
  backplane channel. Leave both unset in production.
- **Reverse proxy.** Behind a reverse proxy, set `Http__ForwardedHeaders__KnownProxies` (or `KnownNetworks`) so the
  rate limiter sees the real client; without it, `X-Forwarded-For` is ignored.
- **Ports.** In the compose stack, every host port has a default and can be moved in `deploy/.env` (see `deploy/.env.example`).
