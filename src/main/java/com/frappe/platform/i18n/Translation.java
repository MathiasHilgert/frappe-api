package com.frappe.platform.i18n;

import java.util.Locale;
import java.util.Objects;

/**
 * One machine-translated text, in the same order as the request that produced it.
 *
 * @param text the translated text
 * @param detectedSourceLanguage the source language the provider detected (mapped to its closest supported locale),
 *     regardless of whether the request named a source language
 */
public record Translation(String text, Locale detectedSourceLanguage) {

    /** Validates the translation. */
    public Translation {
        Objects.requireNonNull(text, "text");
        Objects.requireNonNull(detectedSourceLanguage, "detectedSourceLanguage");
    }
}
