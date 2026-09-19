package com.frappe.platform.web;

/** Who stands behind a session. */
public enum SessionKind {

    /** A person signed in with their own credentials. */
    PERSON,

    /** A shared device of a branch (a till or kitchen screen) operated by staff. */
    TERMINAL,

    /** An anonymous guest, e.g. ordering from a table. */
    GUEST
}
