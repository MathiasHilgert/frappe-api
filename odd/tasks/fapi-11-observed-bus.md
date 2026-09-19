# FAPI-11 — platform: Dispatch commands and queries through an observed bus

Plane: [FAPI-11](https://app.plane.so/nulled-software/browse/FAPI-11/) (module platform, size M). Branch: `feat/fapi-11-observed-bus`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-11`.

## Objective
Every use case is one command or query dispatched through a bus to exactly one handler, returns `Result` for expected failures, and gets a span and RED metrics without any telemetry code in the handler.

## Decisions (from the ticket)
- Public API in `com.frappe.platform` (pure Java, no Spring): `Result` (sealed, `Success` / `Failure` records, `map`, `flatMap`, `mapFailure`, `fold`), `Command<R>`, `Query<R>`, `CommandHandler<C, R>`, `QueryHandler<Q, R>`, `CommandBus#dispatch`, `QueryBus#ask`.
- Handlers are discovered at startup from Spring beans, keyed by the message type resolved with `ResolvableType`. Two handlers for one type fail startup naming both beans; a message without a handler throws `MissingHandlerException` naming the type.
- An observing decorator around both buses creates one Micrometer `Observation` `use_case` per dispatch, outside the handler's `@Transactional` proxy, so the commit happens inside the observation. Span name `<module> <UseCase>`; low-cardinality tags `use_case.name`, `use_case.module`, `use_case.kind`, `outcome` (`success` | `failure` | `error`).
- Command handlers are `@Transactional`; query handlers are not. Revised after review (T6): query handlers are `@Transactional(readOnly = true)`, and a returned `Failure` rolls the command's transaction back.
- Bus and `Result` are hand-written (ticket, Libraries): no maintained library offers a plain in-process CQRS bus without a framework runtime (Axon); Vavr's `Either` would pull a full collections library into the pure domain for one type.

## Out of scope
HTTP, authorization, query caching, async or retried dispatch, any business use case.

## TDD
Strict TDD (project standard, `testing-code`). Runner: `./gradlew test` with `FRAPPE_TEST_DB=frappe_fapi_11` (Testcontainers Postgres). RED observed before every behavior.

## Tasks
- [x] T0 Verify the library APIs used (Spring `ResolvableType`, bean factory lookups, transaction attribute source, Micrometer Observation and its test kit) from the jars in the Gradle cache; record findings and design
- [x] T1 `Result` in the kernel (pure Java)
- [x] T2 Messages, handler interfaces, bus ports; startup discovery keyed by message type; duplicate and missing handler failures
- [x] T3 Observing decorator: one `use_case` observation per dispatch with outcome and error
- [x] T4 Postgres proof: rollback leaves neither state nor outbox row; a commit failure is observed as `error`
- [x] T5 Docs (`writing-code` use cases and observability, `observing-the-api` conventions); verification
- [x] T6 Review decisions: rollback on `Failure`, read-only query transactions enforced at startup, lazy cached handler lookup, `MissingHandlerException` Javadoc, fixture version note

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

### T2 Messages, handlers, discovery
- RED `HandlerDiscoveryTest` (8, `ApplicationContextRunner`, no database): compilation failed, `cannot find symbol` for `Command`, `CommandBus`, `CommandHandler`, `Query`, `QueryBus`, `QueryHandler`, `BusConfiguration`, `InvalidHandlersException`, `MissingHandlerException`.
- GREEN 8/8: A1 `dispatchingACommandRunsItsSingleHandlerOnceAndReturnsItsResultUnchanged` (same `Result` instance, one call); `askingAQueryReturnsWhatItsHandlerReturns`; A2 `twoHandlersForOneTypeFailStartupNamingBothBeans`; A3 `askingAQueryWithoutAHandlerThrowsMissingHandlerExceptionNamingTheType` (and the command twin); startup failures for a command handler without `@Transactional`, a handler whose message type is not a concrete class (generic handler; same path for lambdas), and a message outside `com.frappe.<module>` (fixture `fixtures.bus.OutsideModuleCommand`).
- Code: public ports in `com.frappe.platform`; `infrastructure.bus`: `HandlerRegistry` (bean names and types only, no instantiation; all problems in one `InvalidHandlersException`; one INFO line per kind with `frappe.use_case.kind` / `frappe.use_case.handlers`), `HandlerCommandBus` / `HandlerQueryBus`, `UseCase` / `UseCaseKind`, `BusConfiguration`. `./gradlew javadoc` green.

### T3 Observing decorator
- RED `UseCaseObservationTest` (6, `ApplicationContextRunner` with `TestObservationRegistry` and real transaction proxies over a stub `AbstractPlatformTransactionManager` whose commit can fail): 6/6 failed. Two with `There are no observations registered` (nothing observed yet); four with context startup failing on `InvalidHandlersException: cannot tell which command bean 'useCaseObservationTest.PlaceOrderHandler' handles`. That second failure is a real bug found by the RED: `@EnableTransactionManagement` defaults to JDK proxies, whose class implements the handler interface raw, and the singleton already existed, so `getType` returned `jdk.proxy3.$Proxy72`. Debug output showed the merged bean definition still resolving to the declared class.
- Fix: `HandlerRegistry` asks the merged bean definition (`getResolvableType()`: target type, factory method return type, bean class) first and falls back to the user class of the bean type; so JDK-proxied handlers and `@Bean` methods declaring `CommandHandler<X, R>` resolve too. `HandlerDiscoveryTest` stays 8/8.
- GREEN 6/6: A4 success (`use_case`, contextual name `platform PlaceOrder`, `use_case.name|module|kind`, `outcome=success`, no error, stopped), `Result.Failure` → `outcome=failure` without error, a throwing handler → `outcome=error` with that exception attached and rethrown unchanged, a failing commit → `outcome=error` with the `TransactionSystemException` (the commit runs inside the observation), a query → `use_case.kind=query`, a message without a handler → `outcome=error`.
- Code: `UseCaseObservationContext` (use case + returned value; outcome `unknown` until the handler returns), `UseCaseObservationConvention` (name, contextual name, the four low-cardinality keys), `UseCaseObservations` (`Observation#observe`, no hand-written catch), `ObservedCommandBus` / `ObservedQueryBus`; `BusConfiguration` exposes only the observed buses. `./gradlew javadoc` green.

### T4 Postgres proof
- `UseCaseBusIntegrationTests` (`@SpringBootTest`, Testcontainers Postgres and per-context NATS; handlers are `@Bean`s of a nested `@TestConfiguration`, CGLIB-proxied as in production).
- First run: 3/3 green, but `aCommandFailingOnCommitIsObservedAsAnError` passed for the wrong reason (the table did not exist, so the insert failed inside the handler, which is also `outcome=error`). Tightened: the handler records that it returned, and the timer's `error` tag must name the thrown exception. RED: `Expecting value to be true but was false` (handle never returned). GREEN after the test-only migration `fixture/V202609191100__create_fixture_labelled_probe.sql` (unique label `deferrable initially deferred`): 3/3.
- A5 `aRolledBackCommandLeavesNeitherItsStateNorItsOutboxRow` (probe row and outbox/archive row absent; timer `use_case{use_case.name=RecordProbe, outcome=error, error=IllegalStateException}` counted once) and the control `aCommittedCommandStoresItsStateAndItsOutboxRow` passed on their first run: they guard behaviour that `@Transactional` and the FAPI-6 outbox already provide, so no RED was possible for them.
- `aCommandFailingOnCommitIsObservedAsAnError`: every statement succeeds, the deferred constraint fails the commit in the handler's proxy, and the `use_case` timer records `outcome=error` (never `success`) with the commit exception, proving the commit runs inside the observation end to end.

### T5 Docs and verification
- `writing-code/references/use-cases.md`: real kernel types, the one-step handler declaration, `Result` usage and the failure/error split, the startup rules (one handler per type, `@Transactional` command handlers, concrete message classes in module packages), no telemetry in handlers.
- `writing-code/references/observability.md` and `observing-the-api/references/conventions.md`: `use_case` added to the automatic telemetry, with its tags and how to read `outcome`. `testing-code/references/unit-tests.md`: the example asserted on `isFailure()` / `error()`, which `Result` does not have; now `isEqualTo(Result.failure(...))`. Package docs of `com.frappe.platform` and `com.frappe.platform.infrastructure` mention the bus.
- Verification `FRAPPE_TEST_DB=frappe_fapi_11 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 48 test classes, 192 tests, 0 failures, 0 errors (spotless, javadoc with doclint `-Werror`, `ModularityTests` included).

### T6 Review decisions (orchestrator, review approved without blockers)
- API check (spring-aop / spring-tx 7.0.9 sources): `AbstractAdvisingBeanPostProcessor#postProcessAfterInitialization` appends its advisor to an existing `Advised` proxy (`beforeExistingAdvisors=false`), i.e. innermost, inside the transaction interceptor; the auto-proxy creator is registered with `HIGHEST_PRECEDENCE`, the advising post-processor defaults to `LOWEST_PRECEDENCE`, so the transaction proxy exists when ours runs. `TransactionAspectSupport.currentTransactionStatus()` returns the interceptor's status (else `NoTransactionException`). `determineBeanType` would predict a proxy type of its own, so it is overridden to the bean class, and `isEligible(Object, String)` returns `false`: we only extend existing transaction proxies, never create one.
- RED (4): `UseCaseBusIntegrationTests.aCommandReturningAFailureRollsBackItsStateAndItsOutboxRow` (`expected: 0 but was: 1`, the probe row committed), `UseCaseObservationTest.aFailureResultRollsBackTheTransactionInsteadOfCommittingIt` (`Expecting AtomicInteger(0) to have value: 1`, no rollback), `HandlerDiscoveryTest.aQueryHandlerWithoutAReadOnlyTransactionFailsStartup` (context started), `HandlerDiscoveryTest.aHandlerIsCreatedOnFirstUseNotAtStartupAndThenReused` (prototype handler: `AtomicInteger(2)`, expected 1).
- GREEN: `RollbackOnFailurePostProcessor` (static `@Bean`) marks the transaction rollback-only when `handle` returns a `Result.Failure`; the failure is returned and observed as `outcome=failure` (timer `error=none`). `HandlerRegistry` requires a read-only transaction for queries and a read-write one for commands (bean named in the startup failure), and caches handler instances per message type in a `ConcurrentHashMap` on first use (`get` + `putIfAbsent`, not `computeIfAbsent`, because creating a handler may dispatch). Bus tests 10 + 7 + 4 green.
- Docs: `CommandBus` / `QueryBus` Javadoc (`MissingHandlerException` is a programming error, not to be caught), `CommandHandler` / `QueryHandler` Javadoc, `use-cases.md` (workaround removed, read-only queries, lookup on first use), `persistence.md` (every use case has the transaction `set local` needs), `integration-tests.md` (fixture versions share the production version space).
- Verification `FRAPPE_TEST_DB=frappe_fapi_11 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 48 classes, 196 tests, 0 failures, 0 errors.

## Open questions / follow-ups
- A command handler that joins an outer transaction (e.g. dispatched from inside another transaction) and returns a `Failure` marks the whole transaction rollback-only; the outer commit then fails with `UnexpectedRollbackException`. Handlers are dispatched from outside transactions today, so this is noted, not handled.
- Handlers are resolved once and reused, so a prototype-scoped handler behaves like a singleton.
- `MissingHandlerException` and `InvalidHandlersException` are package-private in `infrastructure.bus` (errors standard).

## Next step
Review and PR (not created here: no push, no Plane change).
