package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNoException;

import java.time.Duration;
import org.junit.jupiter.api.Test;

class OutboxRecoveryPropertiesTest {

    static final Duration INTERVAL = Duration.ofMinutes(1);
    static final int BATCH_SIZE = 100;
    static final Duration STUCK_AFTER = Duration.ofMinutes(5);
    static final int MAX_ATTEMPTS = 24;
    static final Duration MAX_BACKOFF = Duration.ofHours(1);

    @Test
    void acceptsTheDefaults() {
        assertThatNoException()
                .isThrownBy(() ->
                        new OutboxRecoveryProperties(INTERVAL, BATCH_SIZE, STUCK_AFTER, MAX_ATTEMPTS, MAX_BACKOFF));
    }

    @Test
    void rejectsANonPositiveInterval() {
        assertThatIllegalArgumentException()
                .isThrownBy(() ->
                        new OutboxRecoveryProperties(Duration.ZERO, BATCH_SIZE, STUCK_AFTER, MAX_ATTEMPTS, MAX_BACKOFF))
                .withMessageContaining("frappe.outbox.recovery.interval");
    }

    @Test
    void rejectsANonPositiveBatchSize() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new OutboxRecoveryProperties(INTERVAL, 0, STUCK_AFTER, MAX_ATTEMPTS, MAX_BACKOFF))
                .withMessageContaining("frappe.outbox.recovery.batch-size");
    }

    @Test
    void rejectsANonPositiveStuckAfter() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new OutboxRecoveryProperties(
                        INTERVAL, BATCH_SIZE, Duration.ofSeconds(-1), MAX_ATTEMPTS, MAX_BACKOFF))
                .withMessageContaining("frappe.outbox.recovery.stuck-after");
    }

    @Test
    void rejectsANonPositiveMaxAttempts() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new OutboxRecoveryProperties(INTERVAL, BATCH_SIZE, STUCK_AFTER, 0, MAX_BACKOFF))
                .withMessageContaining("frappe.outbox.recovery.max-attempts");
    }

    @Test
    void rejectsANonPositiveMaxBackoff() {
        assertThatIllegalArgumentException()
                .isThrownBy(() ->
                        new OutboxRecoveryProperties(INTERVAL, BATCH_SIZE, STUCK_AFTER, MAX_ATTEMPTS, Duration.ZERO))
                .withMessageContaining("frappe.outbox.recovery.max-backoff");
    }

    @Test
    void rejectsAMaxBackoffShorterThanTheInterval() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new OutboxRecoveryProperties(
                        INTERVAL, BATCH_SIZE, STUCK_AFTER, MAX_ATTEMPTS, Duration.ofSeconds(30)))
                .withMessageContaining("frappe.outbox.recovery.max-backoff")
                .withMessageContaining("frappe.outbox.recovery.interval");
    }
}
