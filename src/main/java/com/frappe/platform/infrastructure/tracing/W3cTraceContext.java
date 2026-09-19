package com.frappe.platform.infrastructure.tracing;

import java.util.Optional;
import java.util.regex.Pattern;

/**
 * A W3C Trace Context (<a href="https://www.w3.org/TR/trace-context/">https://www.w3.org/TR/trace-context/</a>) as it
 * travels with a message: the {@code traceparent} and the vendor-specific {@code tracestate}. Values from outside
 * (message headers, stored rows) go through {@link #parse}, which never throws.
 *
 * @param traceparent {@code <version>-<trace-id>-<parent-id>-<flags>}, lowercase hex, e.g. {@code
 *     00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01}
 * @param tracestate vendor entries passed on unchanged; empty when there are none
 */
public record W3cTraceContext(String traceparent, String tracestate) {

    /** Header and propagation field carrying the {@code traceparent}. */
    public static final String TRACEPARENT = "traceparent";

    /** Header and propagation field carrying the {@code tracestate}. */
    public static final String TRACESTATE = "tracestate";

    // Version ff is forbidden, and all-zero trace and parent ids are invalid. Only the four-field form is accepted,
    // which is every version in use today (00).
    private static final Pattern TRACEPARENT_FORMAT =
            Pattern.compile("(?!ff)[0-9a-f]{2}-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}");

    // The W3C limit a receiver must accept; printable ASCII only. The list-member grammar is not checked: tracestate is
    // passed on, never interpreted.
    private static final int MAX_TRACESTATE_LENGTH = 512;
    private static final Pattern TRACESTATE_FORMAT = Pattern.compile("[\\x20-\\x7e]*");

    private static final int TRACE_ID_START = 3;
    private static final int SPAN_ID_START = 36;
    private static final int FLAGS_START = 53;
    private static final int SAMPLED_FLAG = 0x01;

    /**
     * Creates a trace context from values known to be valid.
     *
     * @throws IllegalArgumentException if the traceparent or the tracestate is malformed
     */
    public W3cTraceContext {
        if (!isValidTraceparent(traceparent)) {
            throw new IllegalArgumentException("Malformed traceparent: " + traceparent);
        }
        if (!isValidTracestate(tracestate)) {
            throw new IllegalArgumentException("Malformed tracestate: " + tracestate);
        }
    }

    /**
     * Reads a trace context from untrusted values. A malformed tracestate is dropped and the traceparent kept, as the
     * W3C specification requires.
     *
     * @param traceparent the {@code traceparent} value, may be {@code null}
     * @param tracestate the {@code tracestate} value, may be {@code null}
     * @return the trace context, or empty if the traceparent is missing or malformed
     */
    public static Optional<W3cTraceContext> parse(String traceparent, String tracestate) {
        if (!isValidTraceparent(traceparent)) {
            return Optional.empty();
        }
        return Optional.of(new W3cTraceContext(traceparent, isValidTracestate(tracestate) ? tracestate : ""));
    }

    /**
     * The trace this context belongs to.
     *
     * @return 32 lowercase hex characters
     */
    public String traceId() {
        return traceparent.substring(TRACE_ID_START, SPAN_ID_START - 1);
    }

    /**
     * The span that was active when the context was taken (W3C: parent id).
     *
     * @return 16 lowercase hex characters
     */
    public String spanId() {
        return traceparent.substring(SPAN_ID_START, FLAGS_START - 1);
    }

    /**
     * Whether the trace was sampled where the context was taken.
     *
     * @return the sampled flag
     */
    public boolean sampled() {
        return (Integer.parseInt(traceparent.substring(FLAGS_START), 16) & SAMPLED_FLAG) != 0;
    }

    private static boolean isValidTraceparent(String traceparent) {
        return traceparent != null && TRACEPARENT_FORMAT.matcher(traceparent).matches();
    }

    private static boolean isValidTracestate(String tracestate) {
        return tracestate != null
                && tracestate.length() <= MAX_TRACESTATE_LENGTH
                && TRACESTATE_FORMAT.matcher(tracestate).matches();
    }
}
