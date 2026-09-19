# Use cases

Package: `com.frappe.<module>.application`. Kernel types (`com.frappe.platform`, plain Java): `@CommandUseCase`, `@QueryUseCase`, `Result<T, E>`.

## Shape

- One class per operation, named for intent (`CloseTab`, `FindOpenTabs`), marked `@CommandUseCase` (changes state) or `@QueryUseCase` (reads state), with exactly one public method. The class is public (the module's `Api` implementation and web adapters call it), everything else about it is not. Callers (controllers, the module's `Api`, listeners) inject the class and call that method directly; there is no command/query bus, as in mainstream Spring (Spring RESTBucks, jMolecules examples).
- The stereotype is all it takes: the platform registers the class as a bean (no `@Component`/`@Service`), observes every call and rolls back a returned failure.
- The method takes the input it needs: plain parameters or a request record named for intent (`CloseTab.Request`).
- Use cases orchestrate: load aggregate → call behavior → save → return. Business rules stay in the domain.
- Commands return `Result<id or small view, Error>`; queries return read models (records), never aggregates.

```java
@CommandUseCase
public class CloseTab {
    private final Tabs tabs;                   // domain port, implemented in persistence
    private final Clock clock;
    private final DomainEventPublisher events; // platform kernel port, writes to the outbox

    CloseTab(Tabs tabs, Clock clock, DomainEventPublisher events) {
        this.tabs = tabs;
        this.clock = clock;
        this.events = events;
    }

    @Transactional
    public Result<TabId, TabError> close(TabId tabId) {
        return tabs.byId(tabId)
                .flatMap(tab -> tab.close(clock))
                .map(tab -> {
                    tabs.save(tab);
                    events.publishAll(tab.pullEvents());
                    return tab.id();
                });
    }
}

@QueryUseCase
public class FindOpenTabs {
    @Transactional(readOnly = true)
    public List<OpenTab> find(BranchId branchId) { … }
}

// Callers inject and call it:
Result<TabId, TabError> closed = closeTab.close(tabId);
```

## Result

- `Result.success(value)` / `Result.failure(error)`; neither holds `null`. Compose with `map`, `flatMap`, `mapFailure` (e.g. domain error → web error); read with `fold` or a `switch` over `Result.Success` / `Result.Failure`.
- `orElseThrow(exceptionOf)` is for the web edge only: a route calls `result.orElseThrow(RequestRefusedException::new)` and the platform answers the failure with its module's mapped problem (`http-api.md`, "Errors"). Inside a module, compose results.
- A returned `Failure` is a business refusal: observed as `outcome=failure`. An exception is a defect or infrastructure fault: `outcome=error`. Never throw for an expected failure, or error alerts stop meaning anything. The one exception: a scheduled task action that must retry a refusal turns it into its own exception with `result.orElseThrow(...)` (`scheduling.md`), because the scheduler retries only on an exception.
- A returned `Failure` rolls back the transaction the use case started: nothing it saved or recorded before refusing persists. When the use case joins a caller's transaction, the caller (its owner) decides from the returned failure.
- A `Failure` never commits the use case's own transaction. State that must survive a refusal (failed-login attempts, rate counters) does not go there: short-lived counters and attempts live in Valkey through `RateLimiter` / `ShortLivedSecretStore` (FAPI-16); anything else durable is written by a separate use case or bean with `@Transactional(propagation = REQUIRES_NEW)`, which commits on its own before the refusal is returned.

## Rules (enforced by `UseCaseArchitectureTests`, ArchUnit)

- A use case is a concrete class carrying `@CommandUseCase` or `@QueryUseCase` directly or through a composed annotation (an annotation meta-annotated with one of them). The marker is not inherited: a subclass is no use case of its own. The scan, the telemetry and the rules all use this same matching.
- Use cases live in `com.frappe.<module>.application..`, carry exactly one of the two stereotypes and have exactly one public method, counting inherited public methods (not `Object`'s, not compiler-generated ones).
- Use cases are proxied (CGLIB): neither the class nor its operation is `final`.
- Transactions: the operation of a `@CommandUseCase` is `@Transactional` (read-write), of a `@QueryUseCase` `@Transactional(readOnly = true)` (on the method or the class), with propagation `REQUIRED` (default) or `REQUIRES_NEW`, so it always runs in a transaction: the command's for atomic state and outbox writes, the query's for the tenant setting of RLS (`persistence.md`). `NESTED` is not allowed: Spring Boot's JPA transaction manager rejects savepoints (`NestedTransactionNotSupportedException`); use `REQUIRES_NEW` for work that must commit on its own.
- Dependencies are an allow-list:
  - domain (`<module>.domain..`): the JDK (`java..`), the kernel (`com.frappe.platform`) and its own module's domain;
  - use cases (`<module>.application..`): the JDK, the kernel, their own module except `infrastructure`, other modules' root packages (their `Api` and events, verified by Spring Modulith), and from Spring exactly `@Transactional`, `Propagation` and `Isolation` (the one accepted Spring annotation with its attribute types);
  - nothing else: no other Spring type and no infrastructure library (Spring Data, Micrometer, OpenTelemetry, Bucket4j, db-scheduler, jnats, Lettuce, ICU4J, JPA, Jackson; not even nullness annotations yet). The kernel itself depends on the JDK only (`KernelDependenciesTest`).
- Use cases never create telemetry; the platform observes every call (`use_case`, see `observability.md`) outside the transaction, so the commit is part of the measured call.
- `platform` is a Spring Modulith shared module (`@Modulithic(sharedModules = "platform")`): a `@ApplicationModuleTest` of any module bootstraps it, so the module's use cases are registered, observed and rolled back in module tests too.
- Constructor injection only; keep use cases stateless: they are singletons, and a prototype scope is not supported.
- Repository interfaces (ports) live in `domain`; implementations in `infrastructure.persistence`.
- A use case touches one aggregate instance per transaction. Cross-aggregate effects go through events.
- Map `Result` failures to HTTP only in the web layer.
- Public `XxxApi` methods delegate to use cases; they never expose domain types, only records in the module root package.
