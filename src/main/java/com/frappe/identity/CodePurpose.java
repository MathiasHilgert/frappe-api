package com.frappe.identity;

/** What a one-time code proves, which decides the mail that carries it. */
public enum CodePurpose {
    /** Proves the mailbox of a new account. */
    SIGN_UP,
    /** Proves the mailbox of a person's new address. */
    EMAIL_CHANGE,
    /** Proves the mailbox before a password reset. */
    PASSWORD_RESET,
    /** Proves the new mailbox of an account being recovered. */
    ACCOUNT_RECOVERY
}
