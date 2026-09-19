package com.frappe.identity.domain;

/** Port: asks a breach corpus whether a password has leaked. Never throws; any failure is {@link BreachStatus#UNKNOWN}. */
@FunctionalInterface
public interface BreachedPasswords {

    /**
     * Looks the password up.
     *
     * @param password the password to check
     * @return what the corpus knows, {@link BreachStatus#UNKNOWN} when it could not be asked
     */
    BreachStatus check(Password password);
}
