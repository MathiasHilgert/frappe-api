# Errors

- Domain and use cases: expected business failures return `Result`; no exceptions (see `use-cases.md`).
- Infrastructure faults: a dedicated, package-private `RuntimeException` per failure kind (`EventPublicationException`, `NatsProvisioningException`, `MissingDatabaseSettingsException`), with an actionable message and the original exception as `cause` whenever there is one.
- Catch only what can actually be thrown and handled: name the types (`IOException | JetStreamApiException | JacksonException`). Never `catch (Exception)` or `catch (RuntimeException)`.
- Log OR rethrow, never both. Log once, at the boundary that handles the failure and knows the context (a background task, a listener turning a failure into a failed future). Everything below it wraps and rethrows.
- `InterruptedException`: restore the flag (`Thread.currentThread().interrupt()`), then finish cleanup (close resources) before returning.
- Fail fast at startup for missing configuration, naming the environment variable to set.
