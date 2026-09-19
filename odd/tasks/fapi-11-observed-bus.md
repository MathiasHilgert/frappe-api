# FAPI-11 — platform: Dispatch commands and queries through an observed bus

Plane: [FAPI-11](https://app.plane.so/nulled-software/browse/FAPI-11/) (module platform, size M). Branch: `feat/fapi-11-observed-bus`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-11`.

## Objective
Every use case is one command or query dispatched through a bus to exactly one handler, returns `Result` for expected failures, and gets a span and RED metrics without any telemetry code in the handler.

## Decisions (from the ticket)
- Public API in `com.frappe.platform` (pure Java, no Spring): `Result` (sealed, `Success` / `Failure` records, `map`, `flatMap`, `mapFailure`, `fold`), `Command<R>`, `Query<R>`, `CommandHandler<C, R>`, `QueryHandler<Q, R>`, `CommandBus#dispatch`, `QueryBus#ask`.
- Handlers are discovered at startup from Spring beans, keyed by the message type resolved with `ResolvableType`. Two handlers for one type fail startup naming both beans; a message without a handler throws `MissingHandlerException` naming the type.
- An observing decorator around both buses creates one Micrometer `Observation` `use_case` per dispatch, outside the handler's `@Transactional` proxy, so the commit happens inside the observation. Span name `<module> <UseCase>`; low-cardinality tags `use_case.name`, `use_case.module`, `use_case.kind`, `outcome` (`success` | `failure` | `error`).
- Command handlers are `@Transactional`; query handlers are not.
- Bus and `Result` are hand-written (ticket, Libraries): no maintained library offers a plain in-process CQRS bus without a framework runtime (Axon); Vavr's `Either` would pull a full collections library into the pure domain for one type.

## Out of scope
HTTP, authorization, query caching, async or retried dispatch, any business use case.

## TDD
Strict TDD (project standard, `testing-code`). Runner: `./gradlew test` with `FRAPPE_TEST_DB=frappe_fapi_11` (Testcontainers Postgres). RED observed before every behavior.

## Tasks
- [ ] T0 Verify the library APIs used (Spring `ResolvableType`, bean factory lookups, transaction attribute source, Micrometer Observation and its test kit) from the jars in the Gradle cache; record findings and design
- [ ] T1 `Result` in the kernel (pure Java)
- [ ] T2 Messages, handler interfaces, bus ports; startup discovery keyed by message type; duplicate and missing handler failures
- [ ] T3 Observing decorator: one `use_case` observation per dispatch with outcome and error
- [ ] T4 Postgres proof: rollback leaves neither state nor outbox row; a commit failure is observed as `error`
- [ ] T5 Docs (`writing-code` use cases and observability, `observing-the-api` conventions); verification

## Acceptance (from ticket)
- A1 One handler for a command: dispatching runs it once and returns its `Result` unchanged.
- A2 Two handlers for the same type: startup fails and names both beans.
- A3 A query with no handler: `MissingHandlerException` names the type.
- A4 Handler returns `Success`, `Failure`, throws, or fails on commit: one stopped observation with outcome `success`, `failure` or `error` (the last two with the exception attached).
- A5 A command handler that saves and publishes events rolls back: neither the state nor the outbox row exists.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_11 ./gradlew spotlessApply check --rerun-tasks`.

## Progress / evidence

## Next step
T0.
