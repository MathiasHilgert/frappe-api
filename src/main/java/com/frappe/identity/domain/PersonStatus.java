package com.frappe.identity.domain;

/** Whether a person can use their account. */
public enum PersonStatus {
    /** The account works; a person is active from the first moment, because the mailbox was proven before. */
    ACTIVE,
    /** The account was erased; only its id remains. */
    ERASED
}
