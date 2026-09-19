package com.frappe.platform.infrastructure.web;

import jakarta.servlet.ServletException;
import org.apache.catalina.connector.ClientAbortException;
import org.apache.coyote.BadRequestException;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.context.request.async.AsyncRequestNotUsableException;

/**
 * Tells failures the client caused from the server's own: a body that cannot be read (truncated, malformed, bad
 * chunking) is a 4xx answer, and a client that went away gets no answer at all; neither is logged as an error or
 * counted. Only the servlet container's own signals and Spring's are trusted, never a bare {@code IOException}, which a
 * database driver throws just as well.
 */
final class ClientFaults {

    private ClientFaults() {}

    /**
     * Whether the client is gone (connection aborted, response no longer usable), so nothing can be answered.
     *
     * @param failure the failure
     * @return whether it or one of its causes says so
     */
    static boolean clientGone(Throwable failure) {
        return causedBy(failure, ClientAbortException.class) || causedBy(failure, AsyncRequestNotUsableException.class);
    }

    /**
     * Whether the request could not be read: a client error, answered with the invalid-request problem.
     *
     * @param failure the failure
     * @return whether it or one of its causes says so
     */
    static boolean unreadableRequest(Throwable failure) {
        // Tomcat's ClientAbortException is a BadRequestException too, but says the client left.
        var badInput = causedBy(failure, BadRequestException.class) && !causedBy(failure, ClientAbortException.class);
        return badInput || causedBy(failure, HttpMessageNotReadableException.class);
    }

    /**
     * The failure behind servlet exception wrappers, which only say that a servlet failed.
     *
     * @param failure the failure
     * @return the innermost cause of the {@code ServletException} wrappers, or the failure itself
     */
    static Throwable unwrap(Throwable failure) {
        var current = failure;
        while (current instanceof ServletException wrapper && wrapper.getRootCause() != null) {
            current = wrapper.getRootCause();
        }
        return current;
    }

    private static boolean causedBy(Throwable failure, Class<? extends Throwable> type) {
        for (var current = failure; current != null; current = current.getCause()) {
            if (type.isInstance(current)) {
                return true;
            }
            if (current.getCause() == current) {
                return false;
            }
        }
        return false;
    }
}
