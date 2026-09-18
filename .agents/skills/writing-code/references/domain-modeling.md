# Domain modeling

Package: `com.frappe.<module>.internal.domain`. No framework imports.

## Aggregates

- `final` class, private fields, no setters, no public constructor; static factories (`open(...)`, `register(...)`) and `reconstitute(...)` for persistence.
- Behavior methods enforce invariants and return `Result<…, XxxError>`; on success they change state and register an event.
- Carry `long version` for optimistic locking; the domain never increments it (persistence does).
- Reference other aggregates by ID only.

```java
public final class Tab {
    private final TabId id;
    private final TenantId tenantId;
    private TabStatus status;
    private final long version;
    private final List<DomainEvent> events = new ArrayList<>();

    public Result<Tab, TabError> close(Clock clock) {
        if (status == TabStatus.CLOSED) return Result.failure(TabError.ALREADY_CLOSED);
        status = TabStatus.CLOSED;
        events.add(new TabClosed(EventId.next(), id, Instant.now(clock)));
        return Result.success(this);
    }

    public List<DomainEvent> pullEvents() { var out = List.copyOf(events); events.clear(); return out; }
}
```

## Value objects

- `record` with validation in the compact constructor; invalid input from users goes through a `static Result<…> of(...)` factory instead of throwing.
- Typed IDs: `record TabId(UUID value)` with `static TabId next()` using UUIDv7.

## Money

`record Money(long minorUnits, Currency currency)`. Arithmetic only between equal currencies; never `double`/`BigDecimal` for amounts in the domain. Rounding rules live in the operation that needs them and are named explicitly.

## Time

- Store and compare `Instant` (UTC). Inject `java.time.Clock` into behavior that needs "now".
- Business day and local time derive from the branch's `ZoneId`, never from the server zone.

## Errors

Domain errors are `enum` or sealed `record` hierarchies per aggregate, named for the rule broken (`ALREADY_CLOSED`, not `INVALID_STATE`).
