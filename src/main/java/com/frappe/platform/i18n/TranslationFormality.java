package com.frappe.platform.i18n;

/**
 * How formal a machine translation should lean, for target languages that support the distinction. A provider that
 * does not support formality for a language pair ignores it and falls back to its default.
 */
public enum TranslationFormality {

    /** The provider's default formality. */
    DEFAULT,

    /** Less formal, more informal. */
    LESS,

    /** More formal. */
    MORE
}
