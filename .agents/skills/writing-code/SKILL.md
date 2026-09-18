---
name: writing-code
description: "Encodes Frappé production code conventions: module anatomy, aggregates, CQRS handlers, JPA/Flyway/RLS, outbox events, REST/ProblemDetail. Use when writing or changing Java under src/main."
license: Proprietary
metadata:
  author: "MathiasHilgert"
  version: "1.0"
---

## Activation Contract

Load before creating or changing any production code, migration or configuration in `src/main`. Load `testing-code` alongside it: tests come first.

## Hard Rules

- Module `com.frappe.<module>`: root package holds only the public `XxxApi` and published events; everything else lives in `domain`, `application`, `infrastructure.{web,persistence}` (subpackages are internal by Modulith rules; no `internal` marker).
- Domain is pure Java: no Spring, JPA, Jackson or Jakarta imports.
- Expected business failures return `Result`; exceptions only for bugs and infrastructure faults.
- Never read another module's tables, entities or internal packages; use its events, or its `Api` for an unavoidable synchronous read.
- IDs are UUIDv7 from the injected `IdGenerator`; money is `Money`; time comes from an injected `Clock`, never `Instant.now()`.
- Infrastructure faults: dedicated exceptions with cause, catch only expected types, log or rethrow (never both).
- Logs are ECS JSON with context in key/values; Javadoc on every type and member (`check` enforces doclint).
- English identifiers and API; user-facing text via message bundles.
- `./gradlew spotlessApply check` green before handing over.

## Decision Gates

| What are you touching? | Reference |
| --- | --- |
| Aggregates, value objects, invariants, IDs, Money, time | `references/domain-modeling.md` |
| Commands, queries, handlers, `Result` | `references/use-cases.md` |
| JPA entities, MapStruct, Flyway, schemas, RLS, locking | `references/persistence.md` |
| Publishing or consuming events, outbox, NATS, inbox | `references/domain-events.md` |
| Controllers, `/v1`, validation, errors, OpenAPI, i18n, sessions, RBAC | `references/http-api.md` |
| Creating ids, reading time | `references/ids.md` |
| Logging | `references/logging.md` |
| Exceptions, catching, interrupts | `references/errors.md` |
| Javadoc, comments, package-info | `references/documentation.md` |
| Any class or test: structure, naming, immutability | `references/clean-code.md` |

Touching several layers: read each matching reference before editing that layer.

## Execution Steps

1. Identify the module and layers the ticket touches; read the matching references.
2. Start from the domain inward-out: domain → application → persistence → web.
3. Add a migration for every schema change; never edit an applied one.
4. Keep the change inside ticket scope; note anything else as a follow-up.

## Output Contract

List files changed per layer, migrations added, events published/consumed, public `Api` changes, and deviations from these rules with the reason.

## References

- `references/*.md` — one file per row of Decision Gates.
