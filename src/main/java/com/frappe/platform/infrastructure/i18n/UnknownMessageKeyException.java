package com.frappe.platform.infrastructure.i18n;

import org.springframework.context.NoSuchMessageException;

/** Code asked for a message key that no catalog defines: a programming error, never a user error. */
final class UnknownMessageKeyException extends RuntimeException {

    private static final String PROBLEM_DETAIL_PREFIX = "problemDetail.";

    /**
     * Creates the exception.
     *
     * @param key the unknown key
     * @param cause Spring's exception, naming the key and the locale
     */
    UnknownMessageKeyException(String key, NoSuchMessageException cause) {
        super("%s; add '%s' to %s".formatted(cause.getMessage(), key, catalogsOf(key)), cause);
    }

    // Keys start with their module's name, so the message can point at the exact files to edit.
    private static String catalogsOf(String key) {
        var dot = key.indexOf('.');
        var module = dot > 0 && !key.startsWith(PROBLEM_DETAIL_PREFIX) ? key.substring(0, dot) : "<module>";
        return "i18n/%s/messages_{en,es,pt}.properties".formatted(module);
    }
}
