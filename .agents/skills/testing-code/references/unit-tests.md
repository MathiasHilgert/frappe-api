# Unit tests

Scope: aggregates, value objects, domain services. Package mirrors production (`com.frappe.<module>.internal.domain`).

## Rules

- Plain JUnit 5 + AssertJ. No Spring, no Mockito for domain objects (use real value objects and a fixed `Clock`).
- Class `<Subject>Test`; method names state behavior: `closingAClosedTabFails`, `moneyOfDifferentCurrenciesCannotBeAdded`.
- One behavior per test, Arrange / Act / Assert separated by blank lines.
- Assert on `Result` outcome, resulting state and registered events.
- Use `@ParameterizedTest` for rule tables (rounding, boundaries).
- Build fixtures with small factory methods in the test class or a `<Aggregate>Fixtures` helper; no shared mutable state.

```java
class TabTest {
    private final Clock clock = Clock.fixed(Instant.parse("2026-01-01T12:00:00Z"), ZoneOffset.UTC);

    @Test
    void closingAClosedTabFails() {
        var tab = TabFixtures.closedTab();

        var result = tab.close(clock);

        assertThat(result.isFailure()).isTrue();
        assertThat(result.error()).isEqualTo(TabError.ALREADY_CLOSED);
        assertThat(tab.pullEvents()).isEmpty();
    }
}
```

## Speed

Unit tests run in milliseconds. If a test needs a container or context, it is not a unit test; move it to `module-tests.md` or `integration-tests.md`.
