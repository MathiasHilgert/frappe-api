package com.frappe.platform.infrastructure.web;

import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import jakarta.servlet.RequestDispatcher;
import jakarta.servlet.http.HttpServletRequest;
import org.jspecify.annotations.Nullable;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.filter.ServerHttpObservationFilter;

/**
 * Records a failure the API answers with the generic internal-error problem: one ERROR log line with the request and
 * the cause (ECS: {@code error.type}, {@code error.message}, {@code error.stack_trace}, plus the trace id), and the
 * counter {@value #METRIC}, tagged with the exception's simple class name only (behind servlet wrappers); the failure is
 * also attached to the request's HTTP server observation ({@code http.server.requests}, tag {@code exception}). This is the one place such a failure is
 * logged; the client never sees any of it.
 */
final class UnexpectedFailures {

    /** Counter of requests answered with the generic internal-error problem. */
    static final String METRIC = "http.server.unexpected.errors";

    private static final Logger LOG = LoggerFactory.getLogger(UnexpectedFailures.class);
    private static final String NO_EXCEPTION = "none";
    private static final String ANONYMOUS = "anonymous";

    private final MeterRegistry meters;

    /**
     * Creates the recorder.
     *
     * @param meters the registry the counter lives in
     */
    UnexpectedFailures(MeterRegistry meters) {
        this.meters = meters;
    }

    /**
     * Logs and counts one unexpected failure of a request.
     *
     * @param failure what was thrown; {@code null} for a server error reported without an exception
     * @param request the failed request (or its error dispatch)
     */
    void record(@Nullable Throwable failure, HttpServletRequest request) {
        var cause = failure == null ? null : ClientFaults.unwrap(failure);
        if (cause != null) {
            ServerHttpObservationFilter.findObservationContext(request).ifPresent(context -> context.setError(cause));
        }
        LOG.atError()
                .addKeyValue(LogFields.HTTP_METHOD, request.getMethod())
                .addKeyValue(LogFields.URL_PATH, originalPath(request))
                .setCause(failure)
                .log("Request failed unexpectedly; answered with the generic internal-error problem");
        Counter.builder(METRIC)
                .description("Requests answered with the generic internal-error problem")
                .tag("error", errorTag(cause))
                .register(meters)
                .increment();
    }

    // Bounded by the code base: the simple class name, never empty (anonymous classes have none).
    private static String errorTag(@Nullable Throwable cause) {
        if (cause == null) {
            return NO_EXCEPTION;
        }
        var name = cause.getClass().getSimpleName();
        return name.isEmpty() ? ANONYMOUS : name;
    }

    // An error dispatch has its own path (/error); the request that failed is in the servlet's error attributes.
    static String originalPath(HttpServletRequest request) {
        return request.getAttribute(RequestDispatcher.ERROR_REQUEST_URI) instanceof String path
                ? path
                : request.getRequestURI();
    }
}
