---
name: testing-code
description: "Defines Frappé testing with strict TDD: pure domain unit tests, @ApplicationModuleTest module tests, Testcontainers Postgres/NATS integration and RLS tests. Use when writing, fixing or reviewing tests."
license: Proprietary
metadata:
  author: "MathiasHilgert"
  version: "1.0"
---

## Activation Contract

Load before writing production code (tests come first), when writing or fixing tests, and when reviewing test coverage of a ticket.

## Hard Rules

- Strict TDD: RED (failing test, observed) → GREEN (minimal code) → REFACTOR. Record test name and RED/GREEN results for the PR.
- Every acceptance criterion of the ticket maps to at least one test.
- Domain unit tests use no Spring context.
- Databases and brokers are real: Testcontainers Postgres and NATS. Never H2, never mocked repositories in module tests.
- Tests do not depend on wall-clock time or order; inject a fixed `Clock`.
- `./gradlew check` (format, tests, `ModularityTests`) is the gate.

## Decision Gates

| What are you testing? | Reference |
| --- | --- |
| Aggregate, value object, domain rule | `references/unit-tests.md` |
| A use case through the module, events published/consumed, boundaries | `references/module-tests.md` |
| Persistence, migrations, RLS, HTTP end to end, NATS | `references/integration-tests.md` |
| Observations, spans, business metrics | `references/observability-tests.md` |

Generic Spring Boot 4 testing detail: skills `321-frameworks-spring-boot-testing-unit-tests`, `322-frameworks-spring-boot-testing-integration-tests`; Modulith test APIs: `305-frameworks-spring-boot-modulith`. Frappé rules here win on conflict.

## Execution Steps

1. Pick the lowest pyramid level that proves the behavior.
2. Write the test, run it, confirm it fails for the expected reason (RED).
3. Write the minimum code to pass (GREEN); rerun.
4. Refactor with tests green; run `./gradlew spotlessApply check`.

## Output Contract

Report per acceptance criterion: test class and method, RED output summary, GREEN result; then the `./gradlew check` result.

## References

- `references/unit-tests.md`
- `references/module-tests.md`
- `references/integration-tests.md`
