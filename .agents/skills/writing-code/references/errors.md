# Errors

- Domain and use cases: expected business failures return `Result`; no exceptions (see `use-cases.md`).
- Infrastructure faults: a dedicated, package-private `RuntimeException` per failure kind (`EventPublicationException`, `NatsProvisioningException`, `MissingDatabaseSettingsException`), with an actionable message and the original exception as `cause` whenever there is one.
- Catch only what can actually be thrown and handled: name the types (`IOException | JetStreamApiException | JacksonException`). Never `catch (Exception)` or `catch (RuntimeException)`, with one exception below.
- Telemetry isolation boundary: recording telemetry (business metrics in `BusinessMetricsRecorder`) may catch `RuntimeException` per metric, because it runs user-declared lambdas and Micrometer registration inside the business call or its after-commit callback. It logs one WARN with `frappe.metric`, `frappe.event_type`, `frappe.event_id` and skips that metric; telemetry never fails or reaches the business caller. No other code uses this exception.
- Log OR rethrow, never both. Log once, at the boundary that handles the failure and knows the context (a background task, a listener turning a failure into a failed future). Everything below it wraps and rethrows.
- `InterruptedException`: restore the flag (`Thread.currentThread().interrupt()`), then finish cleanup (close resources) before returning.
- No shared base exception while the exceptions are package-private; add one only when a caller outside the package needs to catch the family.
- A boundary that turns a failure into a result (e.g. a failed `CompletableFuture`) is where it is logged. Frameworks may log the same failure again (Spring Modulith's INFO for failed listeners); that duplicate is known and accepted.
- Wrap third-party signals at the adapter boundary: jnats' `IllegalStateException` for a closed connection becomes `NatsUnavailableException` in `NatsClient`.
- Fail fast at startup for missing configuration, naming the environment variable to set.
