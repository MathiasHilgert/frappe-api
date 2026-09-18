# Logging

- Console output is ECS JSON (`logging.structured.format.console=ecs` in `application.properties`); the `local` profile sets it empty for human-readable logs.
- Context goes into fields, not only into the message: use the SLF4J fluent API. Boot's ECS formatter writes SLF4J key/values and MDC entries as JSON fields.
- Field names are constants (e.g. `LogFields` in the NATS package), lowercase, dotted and namespaced: `frappe.*` for domain values (`frappe.event_id`), the technology for transport values (`nats.url`, `nats.subject`, `nats.stream`, `nats.connection_event`). ECS nests them by dot. Reuse existing names; add new ones in the same place.
- Never log credentials: URLs are logged redacted to `scheme://host:port` (`NatsProperties.redactedUrl()`); the same applies to exception messages. No tokens, passwords or personal data.
- The message pattern is a constant: `.log("Published {}", type)` or `.log("{}", message)`; never pass dynamic text as the pattern.
- Known duplicate: when the NATS transport fails a publication it logs one WARN with context; Spring Modulith additionally logs its own INFO "Leaving event publication uncompleted". Keep ours (it carries the fields); Modulith's is not ours to change.
- Levels: ERROR = needs a human now; WARN = degraded but self-healing (NATS down, publication retried); INFO = lifecycle (connected, stream created); DEBUG = per-message detail.
- Messages say what happened and what to do ("run 'docker compose up -d nats' or set FRAPPE_NATS_URL").

```java
log.atWarn()
        .addKeyValue(LogFields.EVENT_ID, event.eventId())
        .addKeyValue(LogFields.SUBJECT, subject)
        .setCause(e)
        .log("Publishing {} failed; the publication stays incomplete for retry", type);
```
