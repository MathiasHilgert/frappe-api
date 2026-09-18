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
| Latency, throughput, memory, slow queries | `optimizing-performance` |
| Generic Spring Boot unit / integration tests | `321-frameworks-spring-boot-testing-unit-tests`, `322-frameworks-spring-boot-testing-integration-tests` |
| Generic Spring Modulith questions | `305-frameworks-spring-boot-modulith` |
| Postgres schema, indexes, RLS, query tuning | `supabase-postgres-best-practices` |

Subagents: `.agents/agents/` (`ticket-writer`, `ticket-writer-deep`, `code-reviewer`, `code-reviewer-deep`). `.claude` and `CLAUDE.md` are symlinks to `.agents` and this file.
