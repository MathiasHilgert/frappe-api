package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNoException;

import java.time.Duration;
import org.junit.jupiter.api.Test;

class OutboxArchivePurgePropertiesTest {

    static final Duration RETENTION = Duration.ofDays(30);
    static final int PURGE_BATCH_SIZE = 500;
    static final Duration PURGE_INTERVAL = Duration.ofHours(1);

    @Test
    void acceptsTheDefaults() {
        assertThatNoException()
                .isThrownBy(() -> new OutboxArchivePurgeProperties(RETENTION, PURGE_BATCH_SIZE, PURGE_INTERVAL));
    }

    @Test
    void rejectsANonPositiveRetention() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new OutboxArchivePurgeProperties(Duration.ZERO, PURGE_BATCH_SIZE, PURGE_INTERVAL))
                .withMessageContaining("frappe.outbox.archive.retention");
    }

    @Test
    void rejectsANonPositivePurgeBatchSize() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new OutboxArchivePurgeProperties(RETENTION, 0, PURGE_INTERVAL))
                .withMessageContaining("frappe.outbox.archive.purge-batch-size");
    }

    @Test
    void rejectsANonPositivePurgeInterval() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new OutboxArchivePurgeProperties(RETENTION, PURGE_BATCH_SIZE, Duration.ZERO))
                .withMessageContaining("frappe.outbox.archive.purge-interval");
    }
}
