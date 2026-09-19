# Module tests

Scope: one Spring Modulith module bootstrapped alone, with real Postgres (and NATS when events cross the broker).

## Setup

```java
@ApplicationModuleTest
@Import(TestcontainersConfiguration.class)
class CloseTabModuleTest {
    @Autowired CloseTab closeTab;

    @Test
    void closingATabPublishesTabClosed(Scenario scenario) {
        scenario.stimulate(() -> closeTab.close(tabId))
                .andWaitForEventOfType(TabClosed.class)
                .matchingMappedValue(TabClosed::tabId, tabId)
                .toArrive();
    }
}
```

- Place the test in the module's root test package so Modulith detects the module.
- Default bootstrap mode is `STANDALONE`; use `DIRECT_DEPENDENCIES` only when the test really needs a neighbor, and prefer publishing the neighbor's event via `Scenario.publish(...)`.
- Drive the module through its use cases or public `Api`, not through internals.
- `platform` is a shared module, so `STANDALONE` still bootstraps it: use case registration, telemetry and rollback on failure, the outbox. `probe` (`src/test/java/com/frappe/probe`) is a test-only module that proves this (`ProbeModuleTests`); Spring Modulith sees it through `ProbeModuleApplicationModules` (`src/test/resources/META-INF/spring.factories`), everything else keeps production's module model.

## Events

- Published: `Scenario.stimulate(...).andWaitForEventOfType(...)`.
- Consumed: `Scenario.publish(event).andWaitForStateChange(() -> query)` and assert the resulting state.
- Idempotency: publish the same event twice; assert the effect happens once.
- Outbox: assert the event publication is completed (`EventPublicationRegistry` / `CompletedEventPublications`) after the transaction commits.

## Boundaries

`ModularityTests` (`ApplicationModules.of(FrappeApiApplication.class).verify()`) runs in `./gradlew check`. Never suppress a violation; fix the dependency or expose it through the module's `Api` or events.

Modulith API detail: skill `305-frameworks-spring-boot-modulith`.
