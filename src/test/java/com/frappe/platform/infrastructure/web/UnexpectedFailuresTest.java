package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import jakarta.servlet.ServletException;
import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockHttpServletRequest;

class UnexpectedFailuresTest {

    final SimpleMeterRegistry meters = new SimpleMeterRegistry();
    final UnexpectedFailures failures = new UnexpectedFailures(meters);

    @Test
    void theErrorTagNamesTheFailureBehindServletWrappers() {
        failures.record(new ServletException(new IllegalStateException("boom")), new MockHttpServletRequest());

        assertThat(meters.find(UnexpectedFailures.METRIC)
                        .tag("error", "IllegalStateException")
                        .counter())
                .isNotNull();
    }

    @Test
    void theErrorTagIsNeverEmpty() {
        failures.record(new RuntimeException() {}, new MockHttpServletRequest());
        failures.record(null, new MockHttpServletRequest());

        assertThat(meters.find(UnexpectedFailures.METRIC).counters())
                .extracting(counter -> counter.getId().getTag("error"))
                .containsExactlyInAnyOrder("anonymous", "none");
    }
}
