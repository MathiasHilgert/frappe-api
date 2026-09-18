# Ids and time

- Every id is a UUIDv7 from the kernel interface `com.frappe.platform.IdGenerator` (`UUID newId()`), injected where ids are born (handlers, factories). The domain never calls a library or `UUID.randomUUID()`.
- The default bean (`platform.infrastructure.ids`) uses `uuid-creator`'s monotonic v7 factory on the application `Clock`: ids from one generator strictly increase, even within one millisecond, so they sort by creation and index inserts stay append-only. Monotonicity holds per generator instance, hence one singleton bean; across nodes ids are only roughly time-ordered (clock skew), so never use id order for correctness, use `aggregateVersion`.
- Time comes from the injected `Clock` bean (UTC), never `Instant.now()`.
- An event's `eventId` is created once when the event is raised and never regenerated on retry: deduplication depends on it.

```java
var tab = Tab.open(new TabId(ids.newId()), table, clock);
```

Tests: `TestIds.withClock(fixedClock)` for real v7 ids, or a lambda `() -> knownId` when the exact id matters.
