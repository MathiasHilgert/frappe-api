# Use cases (CQRS bus)

Package: `com.frappe.<module>.application`. Kernel types (`com.frappe.platform`, pure Java): `Command<R>`, `Query<R>`, `CommandHandler<C, R>`, `QueryHandler<Q, R>`, `CommandBus#dispatch`, `QueryBus#ask`, `Result<T, E>`.

## Shape

- One command or query `record` per use case, named for intent: `CloseTab implements Command<Result<TabId, TabError>>`, `FindOpenTabs implements Query<List<OpenTab>>`. The type argument is what the handler returns.
- One handler per command/query. Declaring it is one step: a Spring bean implementing `CommandHandler<C, R>` or `QueryHandler<Q, R>` with concrete types. The bus finds it at startup by the message type; nothing is registered by hand. The domain is not a Spring bean.
- Handlers orchestrate: load aggregate → call behavior → save → return. Business rules stay in the domain.
- Commands return `Result<id or small view, Error>`; queries return read models (records), never aggregates.

```java
record CloseTab(TabId tabId) implements Command<Result<TabId, TabError>> {}

@Component
class CloseTabHandler implements CommandHandler<CloseTab, Result<TabId, TabError>> {
    private final Tabs tabs;          // domain port, implemented in persistence
    private final Clock clock;
    private final DomainEventPublisher events; // platform kernel port, writes to the outbox

    @Override
    @Transactional
    public Result<TabId, TabError> handle(CloseTab cmd) {
        return tabs.byId(cmd.tabId())
                .flatMap(tab -> tab.close(clock))
                .map(tab -> {
                    tabs.save(tab);
                    events.publishAll(tab.pullEvents());
                    return tab.id();
                });
    }
}

// Callers (controllers, the module's Api) only see the bus:
Result<TabId, TabError> closed = commandBus.dispatch(new CloseTab(tabId));
```

## Result

- `Result.success(value)` / `Result.failure(error)`; neither holds `null`. Compose with `map`, `flatMap`, `mapFailure` (e.g. domain error → web error); read with `fold` or a `switch` over `Result.Success` / `Result.Failure`.
- A returned `Failure` is a business refusal: the bus reports it as `outcome=failure`. An exception is a defect or infrastructure fault: `outcome=error`. Never throw for an expected failure, or error alerts stop meaning anything.
- A `Failure` does not roll back by itself: return it before saving (the `flatMap` chain above), never after a partial save.

## Rules

- `@Transactional` on command handlers only (on `handle` or the class); startup fails for a command handler without it. Saving the aggregate and its events happens in that transaction (see `domain-events.md`). Query handlers are not transactional.
- Exactly one handler per message type: two handlers for one type fail startup naming both beans; a message without a handler throws `MissingHandlerException` naming the type. Handlers are keyed by the exact record class; declare them as classes (not lambdas or generic classes) so the message type resolves. Messages live in `com.frappe.<module>…`.
- Handlers never create telemetry. The bus observes every dispatch (`use_case`, see `observability.md`) outside the handler's transaction proxy, so the commit is part of the measured use case.
- Repository interfaces (ports) live in `domain`; implementations in `infrastructure.persistence`.
- A handler touches one aggregate instance per transaction. Cross-aggregate effects go through events.
- Map `Result` failures to HTTP only in the web layer.
- Public `XxxApi` methods delegate to the bus; they never expose domain types, only records in the module root package.
