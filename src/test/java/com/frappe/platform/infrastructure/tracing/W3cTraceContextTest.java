package com.frappe.platform.infrastructure.tracing;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.NullAndEmptySource;
import org.junit.jupiter.params.provider.ValueSource;

class W3cTraceContextTest {

    private static final String TRACE_ID = "4bf92f3577b34da6a3ce929d0e0e4736";
    private static final String SPAN_ID = "00f067aa0ba902b7";
    private static final String SAMPLED = "00-" + TRACE_ID + "-" + SPAN_ID + "-01";
    private static final String NOT_SAMPLED = "00-" + TRACE_ID + "-" + SPAN_ID + "-00";

    @Test
    void parsesATraceparentWithItsTracestate() {
        // When
        var context = W3cTraceContext.parse(SAMPLED, "rojo=00f067aa0ba902b7,congo=t61rcWkgMzE");

        // Then
        assertThat(context).hasValueSatisfying(it -> {
            assertThat(it.traceparent()).isEqualTo(SAMPLED);
            assertThat(it.tracestate()).isEqualTo("rojo=00f067aa0ba902b7,congo=t61rcWkgMzE");
            assertThat(it.traceId()).isEqualTo(TRACE_ID);
            assertThat(it.spanId()).isEqualTo(SPAN_ID);
            assertThat(it.sampled()).isTrue();
        });
    }

    @Test
    void readsTheSampledFlag() {
        // When / Then
        assertThat(W3cTraceContext.parse(NOT_SAMPLED, null))
                .hasValueSatisfying(it -> assertThat(it.sampled()).isFalse());
    }

    @ParameterizedTest
    @NullAndEmptySource
    void treatsAMissingTracestateAsEmpty(String tracestate) {
        // When / Then
        assertThat(W3cTraceContext.parse(SAMPLED, tracestate))
                .hasValueSatisfying(it -> assertThat(it.tracestate()).isEmpty());
    }

    @ParameterizedTest
    @NullAndEmptySource
    @ValueSource(
            strings = {
                "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7",
                "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-extra",
                "00-4BF92F3577B34DA6A3CE929D0E0E4736-00f067aa0ba902b7-01",
                "ff-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                "00-00000000000000000000000000000000-00f067aa0ba902b7-01",
                "00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01",
                "00-4bf92f3577b34da6a3ce929d0e0e473x-00f067aa0ba902b7-01",
                "00_4bf92f3577b34da6a3ce929d0e0e4736_00f067aa0ba902b7_01",
                "garbage"
            })
    void rejectsAMalformedTraceparent(String traceparent) {
        // When / Then
        assertThat(W3cTraceContext.parse(traceparent, "rojo=00f067aa0ba902b7")).isEmpty();
    }

    @Test
    void dropsAMalformedTracestateButKeepsTheTraceparent() {
        // Given a tracestate with a control character and one longer than the W3C limit of 512 characters
        var withControlCharacter = "rojo=00f067aa0ba902b7\n";
        var tooLong = "rojo=" + "a".repeat(508);

        // When / Then
        assertThat(W3cTraceContext.parse(SAMPLED, withControlCharacter))
                .hasValueSatisfying(it -> assertThat(it.tracestate()).isEmpty());
        assertThat(W3cTraceContext.parse(SAMPLED, tooLong))
                .hasValueSatisfying(it -> assertThat(it.tracestate()).isEmpty());
    }

    @Test
    void refusesToBeCreatedFromInvalidValues() {
        // When / Then
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new W3cTraceContext("garbage", ""))
                .withMessageContaining("traceparent");
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new W3cTraceContext(SAMPLED, "rojo\n"))
                .withMessageContaining("tracestate");
    }
}
