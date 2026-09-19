package com.frappe.platform.infrastructure.events;

import io.micrometer.core.instrument.Gauge;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.core.instrument.binder.MeterBinder;
import java.util.concurrent.atomic.AtomicLong;

/**
 * Gauge {@code outbox.dead.letters} (infrastructure telemetry, named by technology like {@code nats.publish}):
 * publications waiting in {@code platform.event_publication_dead_letter} for a human. Refreshed by every recovery run,
 * so a scrape never queries the database; alert on any value above zero.
 */
final class DeadLetterMetrics implements MeterBinder {

    /** Meter name, stable for dashboards and alerts. */
    static final String DEAD_LETTERS = "outbox.dead.letters";

    private final AtomicLong deadLetters = new AtomicLong();

    /** Creates the metrics with a count of zero until the first recovery run. */
    DeadLetterMetrics() {}

    /**
     * Records the current number of dead letters.
     *
     * @param count rows in the dead-letter table
     */
    void recordDeadLetters(long count) {
        deadLetters.set(count);
    }

    @Override
    public void bindTo(MeterRegistry registry) {
        Gauge.builder(DEAD_LETTERS, deadLetters, AtomicLong::get)
                .description("Event publications given up by the outbox recovery, waiting for a manual replay")
                .baseUnit("publications")
                .register(registry);
    }
}
