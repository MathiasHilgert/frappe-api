# Frappé API — agent index

Java 25 / Spring Boot 4.1 / Spring Modulith 2.1 modular monolith for multi-branch restaurant chains. Package `com.frappe`. Standards live in Plane (Engineering Standards, Architecture, Ticket Standard); skills in `.agents/skills` encode them.

## Golden rules

1. One Plane ticket = one branch = one small squash-merged PR (~400 changed lines).
2. Pure domain: no Spring or JPA in `domain`; expected failures return `Result`, never exceptions.
3. Modules talk through events (outbox → NATS); never touch another module's tables or internals.
4. Strict TDD: RED → GREEN → REFACTOR; Testcontainers with real Postgres/NATS, never H2.
5. `./gradlew check` is the gate, locally and in CI.
6. Conventional Commits, English only, no AI attribution in commits or PRs.
7. Never commit secrets; `PLANE_API_KEY` comes from the environment.

## Routing

| Situation | Skill |
| --- | --- |
| Pick up, implement, review, ship or sync a Plane ticket | `working-on-tickets` |
| Write or change production code (domain, use cases, persistence, events, HTTP) | `writing-code` |
| Write or fix tests, TDD evidence | `testing-code` |
| Business metrics, telemetry, dashboards, SLOs, alerts | `observing-the-api` |
| Latency, throughput, memory, slow queries | `optimizing-performance` |
| Generic Spring Boot unit / integration tests | `321-frameworks-spring-boot-testing-unit-tests`, `322-frameworks-spring-boot-testing-integration-tests` |
| Generic Spring Modulith questions | `305-frameworks-spring-boot-modulith` |
| Postgres schema, indexes, RLS, query tuning | `supabase-postgres-best-practices` |
| OTLP pipeline, PromQL, label cardinality, SLO templates (generic; ours win) | `opentelemetry`, `promql`, `prometheus-label-strategy`, `slo-implementation` |
| Generic Java logging, Micrometer, OTel tracing, exception handling (ours win) | `181-java-observability-logging`, `182-java-observability-metrics-micrometer`, `183-java-observability-tracing-opentelemetry`, `126-java-exception-handling` |

Subagents: `.agents/agents/` (`ticket-writer`, `ticket-writer-deep`, `code-reviewer`, `code-reviewer-deep`). `.claude` and `CLAUDE.md` are symlinks to `.agents` and this file.
