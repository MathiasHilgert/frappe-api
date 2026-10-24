# Deploy

The image (`deploy/Dockerfile`) is framework-dependent on the chiseled ASP.NET runtime, runs as non-root and
listens on port 8080. Outside Development the app creates no schema at startup, so every deploy runs the same image
once with `resources setup` before starting the new version:

```sh
just image
docker run --rm -e ConnectionStrings__frappe="Host=…" frappe-api:local resources setup   # Wolverine and Quartz schemas
docker run -d -p 8080:8080 \
  -e ConnectionStrings__frappe="Host=…" \
  -e ConnectionStrings__valkey="host:6379" \
  -e OTEL_EXPORTER_OTLP_ENDPOINT="http://collector:4317" \
  frappe-api:local
```

| Setting | Required outside Development | Notes |
| --- | --- | --- |
| `ConnectionStrings__frappe` | Yes | Postgres; Critical for readiness |
| `ConnectionStrings__valkey` | Yes | Startup fails without it; NonCritical for readiness |
| `Identity__SecretHashing__Key` | Yes | HMAC key for session tokens and codes, 32+ characters; startup fails without it. Rotating it signs everyone out |
| `Identity__SessionCookie__Name` | No | Session cookie name for the web console, default `__Host-frappe_session`; must be a valid cookie token or startup fails. A `__Host-` name requires Path `/` and no Domain |
| `Identity__SessionCookie__Path` | No | Session cookie path, default `/`; must start with `/` |
| `Identity__SessionCookie__Domain` | No | Unset by default (host-only cookie); not allowed with a `__Host-` name |
| `Messaging__Seal__Key` | Yes | Key (32+ characters) that seals one-time codes inside queued messages; startup fails without it, and outside Development also when it is the committed development key (compared as bytes, so whitespace, case or Base64 variants of it are refused too). Each seal names its key, so it rotates: see below |
| `Messaging__Seal__PreviousKey` | No | The key being rotated out: seals made under it still open, new ones never use it. Remove it once queued codes have drained (a day is generous; codes expire in 15 minutes) |
| `Identity__PasswordHashing__*` | No | Argon2id cost (`MemorySizeKib`, `Iterations`, `Parallelism`); startup fails below the OWASP minimum. `MaxConcurrentHashes` (default: CPU count, 1..1024) caps hashes running at once; extra sign-ins wait at most `SlotWaitMilliseconds` (default 2000) for a slot, then get a 503 with `Retry-After` |
| `Platform__RateLimiting__PermitLimit`, `__WindowSeconds` | No | In-process per-IP cap on anonymous auth endpoints, default 300 per 60 s |
| `Notification__Resend__ApiKey` | Yes | Resend API key (secret, never committed); startup fails without it outside Development. Development never uses it: mail goes to Mailpit (`Notification__Smtp__Host`, set in appsettings.Development.json) or, with that blank, to the log (recipient domain, kind, subject with digits masked; never the body or code). Each delivery sends the Wolverine message id as `Idempotency-Key`, so retries never send twice |
| `Notification__Resend__Timeout` | No | Per-attempt timeout as a `TimeSpan`, default `00:00:10` (1 s to 2 min); the durable queue owns retries |
| `Notification__Smtp__Host`, `__Port`, `__From` | Development only | The local Mailpit; read only in Development, ignored everywhere else |
| `Identity__BreachedPasswords__Timeout` | No | How long sign-up waits for HaveIBeenPwned before failing open, `TimeSpan`, default `00:00:01.5` (100 ms to 10 s) |
| `Identity__AttemptLimiter__Timeout` | No | How long a limiter call waits on Valkey before failing open, `TimeSpan`, default `00:00:00.25` (10 ms to 5 s) |
| `Notification__Resend__From` | No | Sender address, default `Frappé <no-reply@frappe.la>`; must be a domain verified in Resend |
| `ConnectionStrings__nats` | No | Defaults to a local URL; NonCritical, realtime degrades when down |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | No | Traces, metrics and logs over OTLP |
| `ForwardedHeaders__KnownProxies__0`, `__1`… | Behind a proxy | IPs of the reverse proxies whose `X-Forwarded-For`/`-Proto` are trusted (loopback always is). Unset, the socket address is the client: per-IP rate limits would see only the proxy |
| `ForwardedHeaders__KnownNetworks__0`, `__1`… | Behind a proxy | Same, as CIDR ranges (e.g. `10.0.0.0/8`) |

Module EF migrations ship as migration bundles and also run before the app starts.
The orchestrator probes `/health/live` and `/health/ready`; the image has no `HEALTHCHECK` (no shell).

## Rotating the message seal key

1. Deploy with the new key in `Messaging__Seal__Key` and the old one in `Messaging__Seal__PreviousKey`.
2. Wait until no message sealed by the old key can be queued (a day is generous).
3. Deploy again without `Messaging__Seal__PreviousKey`.

Each sealed secret starts with a format version and the first byte of its key's fingerprint, so opening picks the
key without trial and error; seals from before rotation existed (no header) still open under either key.

