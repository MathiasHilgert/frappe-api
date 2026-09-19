package com.frappe.identity.application;

/** Why identity refused a request: the business failures its use cases return. */
public sealed interface IdentityRefusal {

    /** A rate limit refused the attempt. */
    record TooManyAttempts() implements IdentityRefusal {}
}
