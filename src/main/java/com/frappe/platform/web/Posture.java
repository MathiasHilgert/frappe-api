package com.frappe.platform.web;

/**
 * Whether a route needs an authenticated caller. Enforced before any controller code runs; a request the posture
 * refuses never reaches it.
 *
 * <p>The HTTP layer authenticates only. Whether the caller may perform the operation (roles per branch) is
 * authorization, decided by the application layer (use cases and the bus) with the access module. Operations with
 * only internal callers simply have no route.
 */
public enum Posture {

    /** Anyone, with or without a session. */
    PUBLIC,

    /**
     * Any caller with a resolved session; anonymous callers get 401. The use case receives the caller's
     * {@link ResolvedSession} and authorizes the operation itself.
     */
    AUTHENTICATED
}
