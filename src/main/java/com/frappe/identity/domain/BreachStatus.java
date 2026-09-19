package com.frappe.identity.domain;

/** What a breach corpus knows about a password. */
public enum BreachStatus {
    /** The password appears in a breach. */
    BREACHED,
    /** The corpus answered and does not list the password. */
    NOT_FOUND,
    /** The corpus could not be asked; the check fails open. */
    UNKNOWN
}
