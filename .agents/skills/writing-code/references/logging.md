# Logging

- Console output is ECS JSON (`logging.structured.format.console=ecs` in `application.properties`); the `local` profile sets it empty for human-readable logs.
- Context goes into fields, not only into the message: use the SLF4J fluent API. Boot's ECS formatter writes SLF4J key/values and MDC entries as JSON fields.
- Field names are constants (e.g. `LogFields` in the NATS package): `eventId`, `subject`, `stream`, `natsUrl`. Reuse existing names; add new ones in the same place.
- Levels: ERROR = needs a human now; WARN = degraded but self-healing (NATS down, publication retried); INFO = lifecycle (connected, stream created); DEBUG = per-message detail.
- Messages say what happened and what to do ("run 'docker compose up -d nats' or set FRAPPE_NATS_URL"). Never log secrets or personal data.

```java
log.atWarn()
        .addKeyValue(LogFields.EVENT_ID, event.eventId())
        .addKeyValue(LogFields.SUBJECT, subject)
        .setCause(e)
        .log("Publishing {} failed; the publication stays incomplete for retry", type);
```
