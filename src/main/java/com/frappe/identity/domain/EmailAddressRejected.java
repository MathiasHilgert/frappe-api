package com.frappe.identity.domain;

/** Why an email address was refused. */
public enum EmailAddressRejected {
    /** More than {@value EmailAddress#MAX_LENGTH} characters after trimming. */
    TOO_LONG,
    /** Not exactly one {@code @} between a non-empty local part and a non-empty domain. */
    MALFORMED
}
