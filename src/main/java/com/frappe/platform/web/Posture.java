package com.frappe.platform.web;

/** Who may call a route. Enforced before any controller code runs; a request the posture refuses never reaches it. */
public enum Posture {

    /** Anyone, with or without a session. */
    PUBLIC,

    /**
     * Any caller with a resolved session. The use case acts only on the caller's own principal (the "self" posture),
     * so no further check is needed.
     */
    AUTHENTICATED,

    /**
     * A caller with a resolved session that holds the permission named by {@link Access#permission()}, as decided by
     * the permission evaluator.
     */
    PERMISSION,

    /**
     * Nobody over HTTP: the use case has internal callers only, so every HTTP request is refused whatever the
     * session.
     */
    SYSTEM
}
