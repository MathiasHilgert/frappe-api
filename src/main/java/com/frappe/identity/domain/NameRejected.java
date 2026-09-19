package com.frappe.identity.domain;

/** Why a given or family name was refused. */
public enum NameRejected {
    /** Nothing but white space. */
    EMPTY,
    /** More than {@value PersonName#MAX_LENGTH} characters. */
    TOO_LONG
}
