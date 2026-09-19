package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNoException;

import java.time.Duration;
import org.junit.jupiter.api.Test;

class SchedulingPropertiesTest {

    @Test
    void acceptsTheDefaults() {
        assertThatNoException().isThrownBy(() -> new SchedulingProperties(Duration.ofSeconds(30), 5));
    }

    @Test
    void acceptsNoFastRetries() {
        assertThatNoException().isThrownBy(() -> new SchedulingProperties(Duration.ofSeconds(30), 0));
    }

    @Test
    void rejectsANonPositiveInitialBackoff() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new SchedulingProperties(Duration.ZERO, 5))
                .withMessageContaining("frappe.scheduling.initial-backoff");
    }

    @Test
    void rejectsNegativeMaxRetries() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new SchedulingProperties(Duration.ofSeconds(30), -1))
                .withMessageContaining("frappe.scheduling.max-retries");
    }
}
