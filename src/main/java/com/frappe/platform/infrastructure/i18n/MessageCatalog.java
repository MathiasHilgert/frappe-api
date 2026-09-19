package com.frappe.platform.infrastructure.i18n;

import java.util.Locale;
import java.util.Map;

/**
 * One catalog file: the messages of one module in one locale, from {@code i18n/<module>/messages_<locale>.properties}.
 *
 * @param module the module the catalog belongs to (its directory name)
 * @param locale the catalog's locale (from its file name)
 * @param messages ICU message patterns by key
 */
record MessageCatalog(String module, Locale locale, Map<String, String> messages) {

    /**
     * Copies the messages.
     *
     * @param module the module the catalog belongs to
     * @param locale the catalog's locale
     * @param messages ICU message patterns by key
     */
    MessageCatalog {
        messages = Map.copyOf(messages);
    }
}
