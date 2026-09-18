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

- Module `com.frappe.<module>`: root package holds only the public `XxxApi` and published events; everything else lives in `internal.domain`, `internal.application`, `internal.infrastructure.{web,persistence}`.
- Domain is pure Java: no Spring, JPA, Jackson or Jakarta imports.
- Expected business failures return `Result`; exceptions only for bugs and infrastructure faults.
- Never read another module's tables, entities or internal packages; use its events, or its `Api` for an unavoidable synchronous read.
- IDs are UUIDv7 created in the domain; money is `Money`; time comes from an injected `Clock`, never `Instant.now()`.
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

Touching several layers: read each matching reference before editing that layer.

## Execution Steps

1. Identify the module and layers the ticket touches; read the matching references.
2. Start from the domain inward-out: domain → application → persistence → web.
3. Add a migration for every schema change; never edit an applied one.
4. Keep the change inside ticket scope; note anything else as a follow-up.

## Output Contract

List files changed per layer, migrations added, events published/consumed, public `Api` changes, and deviations from these rules with the reason.

## References

- `references/domain-modeling.md`
- `references/use-cases.md`
- `references/persistence.md`
- `references/domain-events.md`
- `references/http-api.md`
