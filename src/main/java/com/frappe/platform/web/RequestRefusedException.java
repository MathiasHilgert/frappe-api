package com.frappe.platform.web;

import java.util.Objects;

/**
 * Thrown by a route to answer a business failure: the platform maps the failure through its {@link ProblemMapper} to a
 * localized problem. It is the web edge's hand-over to the error handling, never a fault: it is not logged as an error,
 * and the use case already recorded the refusal as outcome {@code failure}.
 *
 * {@snippet :
 * var tab = closeTab.close(tabId).orElseThrow(RequestRefusedException::new);
 * }
 */
public final class RequestRefusedException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /** The failure; transient, because failures need not be serializable and the exception never leaves the JVM. */
    private final transient Object failure;

    /**
     * Creates the exception.
     *
     * @param failure the business failure the use case returned, not {@code null}
     */
    public RequestRefusedException(Object failure) {
        super("Request refused with a failure of type "
                + Objects.requireNonNull(failure, "failure").getClass().getName());
        this.failure = failure;
    }

    /**
     * The business failure.
     *
     * @return the failure the use case returned
     */
    public Object failure() {
        return failure;
    }
}
