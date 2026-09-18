# Use cases (CQRS bus)

Package: `com.frappe.<module>.application`.

## Shape

- One command or query `record` per use case, named for intent: `CloseTab`, `FindOpenTabs`.
- One handler per command/query, dispatched through the bus. Handlers are Spring beans; the domain is not.
- Handlers orchestrate: load aggregate → call behavior → save → return. Business rules stay in the domain.
- Commands return `Result<id or small view, Error>`; queries return read models (records), never aggregates.

```java
@Component
class CloseTabHandler implements CommandHandler<CloseTab, Result<TabId, TabError>> {
    private final Tabs tabs;          // domain port, implemented in persistence
    private final Clock clock;
    private final DomainEventPublisher events; // platform kernel port, writes to the outbox

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
```

## Rules

- `@Transactional` on command handlers only; saving the aggregate and its events happens in that transaction (see `domain-events.md`).
- Repository interfaces (ports) live in `domain`; implementations in `infrastructure.persistence`.
- A handler touches one aggregate instance per transaction. Cross-aggregate effects go through events.
- Map `Result` failures to HTTP only in the web layer.
- Public `XxxApi` methods delegate to the bus; they never expose domain types, only records in the module root package.
