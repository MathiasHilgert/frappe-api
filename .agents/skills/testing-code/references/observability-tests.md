# Observability tests

## Business metrics (every feature that declares one)

```java
@Test
void declaresValidMetrics() {
    assertValidBusinessMetrics(TabClosed.class);           // same rules as startup, no Spring
}

@Test
void countsClosedTabs() {                                   // module or integration test
    transactions.executeWithoutResult(s -> events.publishEvent(tabClosed(Channel.DINE_IN)));
    assertThatBusinessMetric(registry, "frappe.order.tabs.closed").withTag("channel", "dine_in").hasCount(1);
}
```

Both live in `com.frappe.platform.infrastructure.metrics.BusinessMetricAssert` (test sources). Contexts are cached and meters accumulate: use a tag value no other test in the context records, or assert the difference.

## Observations in adapters

Unit-test the adapter with `TestObservationRegistry` (`micrometer-observation-test`):

```java
var observations = TestObservationRegistry.create();
// exercise the adapter built with `observations`
TestObservationRegistryAssert.assertThat(observations)
        .hasSingleObservationThat()
        .hasNameEqualTo("nats.publish")
        .hasLowCardinalityKeyValue("messaging.destination.name", subject)
        .hasBeenStopped();
```

Assert names, low-cardinality keys (metric tags), high-cardinality keys (span only), error and stop.

## Spans end to end

- `@SpringBootTest` disables tracing and metric export; opt in with `@AutoConfigureTracing` (and `@AutoConfigureMetrics` when export matters).
- Register an `InMemorySpanExporter` (`opentelemetry-sdk-testing`) as a `@Bean` in a nested `@TestConfiguration`, set `management.opentelemetry.tracing.export.schedule-delay=50ms`, `reset()` it in `@BeforeEach`, and wait with Awaitility.
- Test-only controllers are `@Bean`s of that nested configuration, never component-scanned classes (they would leak into every context).
- Spring Modulith observes only controllers, module API types and listeners of other modules' events, and ignores test classes: prove listener spans with real module code.
