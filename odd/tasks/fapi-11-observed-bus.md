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
- [x] T0 Verify the library APIs used (Spring `ResolvableType`, bean factory lookups, transaction attribute source, Micrometer Observation and its test kit) from the jars in the Gradle cache; record findings and design
- [x] T1 `Result` in the kernel (pure Java)
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

### T0 findings (verified from the sources jars in the Gradle cache: spring-core, spring-beans, spring-aop, spring-tx 7.0.9; micrometer-observation and micrometer-observation-test 1.17.1; spring-boot-test 4.1.1)
- `ResolvableType.forClass(Class<?> baseType, Class<?> implementationClass)` returns the base type as seen from the implementation (`forType(impl).as(base)`); `getGeneric(0).resolve()` yields the message class or `null` when the implementation leaves it generic.
- `ListableBeanFactory#getBeanNamesForType(Class)` and `BeanFactory#getType(String)` find handler beans and their types without instantiating them (no cycle when a handler depends on a bus). A `@Transactional` bean's type may be its CGLIB subclass; `ClassUtils.getUserClass` returns the declared class, whose generics are intact. Boot proxies by class, so JDK proxies (which would lose the generics) do not occur by default; an unresolvable handler type fails startup with an actionable message instead of being skipped.
- `AnnotationTransactionAttributeSource` (public methods only, the proxy default) answers `hasTransactionAttribute(Method, Class)`; `computeTransactionAttribute` resolves the interface method to the implementation via `AopUtils.getMostSpecificMethod` → `BridgeMethodResolver`, and falls back to class-level `@Transactional`. That is exactly the rule Spring's transaction proxy applies, so the startup check cannot disagree with runtime behaviour.
- Micrometer: `Observation.createNotStarted(customConvention, defaultConvention, contextSupplier, registry)` returns a no-op (scope-handling) observation for a no-op registry; `SimpleObservation` asks the convention for key values at `start()` **and** `stop()`, so an outcome derived from the context (result or error) is final at stop. `Observation#observe(Supplier)` starts, opens a scope, records any `Throwable` with `error(...)`, rethrows and stops in `finally`; no hand-written broad catch is needed. `DefaultMeterObservationHandler` turns the observation into the timer `use_case` (plus `use_case.active`), tagged with the low-cardinality keys and `error`.
- Test kit: `TestObservationRegistry`, `TestObservationRegistryAssert.hasSingleObservationThat()`, `hasNameEqualTo`, `hasContextualNameEqualTo`, `hasLowCardinalityKeyValue`, `hasError(Throwable)`, `doesNotHaveError`, `hasBeenStopped`. `ApplicationContextRunner` (spring-boot-test) for startup behaviour without a database.
- No CQRS or `Either` library is in the dependency graph (no Vavr, Axon, PipelinR); the ticket's justification for hand-written `Result` and bus stands.

### Design (T0)
- Package `com.frappe.platform.infrastructure.bus` (internal): `HandlerRegistry` (built once per kind at startup; problems collected and thrown together as `InvalidHandlersException`: duplicates naming every bean, unresolvable handler types, command handlers without a transaction, messages outside a module package), routing buses `HandlerCommandBus` / `HandlerQueryBus` (look up and invoke; `MissingHandlerException`), decorators `ObservedCommandBus` / `ObservedQueryBus`, and the Micrometer idiom `UseCaseObservationContext` + `UseCaseObservationConvention`. `BusConfiguration` exposes only the decorated buses as beans.
- The transaction rule is enforced for command handlers at startup (a command handler without `@Transactional` would save state and outbox rows non-atomically); the query rule stays a convention, documented in `use-cases.md`.
- Outcome: `error` when the dispatch throws (including a failing commit, which surfaces from the handler's proxy inside the observation), `failure` when the result is a `Result.Failure`, `success` otherwise; at start it is `unknown`, like Spring's HTTP convention before the response exists.
- Module and use case names derive from the message type (`com.frappe.<module>…`, simple name); handlers never see telemetry.

### T1 Result
- RED `ResultTest` (8): compilation failed, `package com.frappe.platform.Result does not exist` (35 errors).
- GREEN 8/8: sealed `Result` with `Success` / `Failure` records (null rejected in both, so a mapper returning `null` fails fast), `map`, `flatMap`, `mapFailure`, `fold`, factories `success` / `failure`; pattern matching over the records works. `./gradlew javadoc` green (doclint all, `-Werror`).

## Next step
T2.
