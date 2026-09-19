package com.frappe.platform.i18n;

import java.util.Locale;
import java.util.Optional;

/**
 * One problem in a message catalog, located precisely enough to fix it: module, locale and, when it concerns one
 * message, its key.
 *
 * @param module the module whose catalog is wrong
 * @param locale the locale of the catalog
 * @param key the key of the offending message, or empty when the whole catalog is wrong
 * @param problem what is wrong, phrased to follow the key (or the catalog)
 */
record CatalogViolation(String module, Locale locale, Optional<String> key, String problem) {

    /**
     * Creates a violation of one message.
     *
     * @param module the module whose catalog is wrong
     * @param locale the locale of the catalog
     * @param key the key of the offending message
     * @param problem what is wrong with it
     * @return the violation
     */
    static CatalogViolation ofMessage(String module, Locale locale, String key, String problem) {
        return new CatalogViolation(module, locale, Optional.of(key), problem);
    }

    /**
     * Creates a violation of a whole catalog.
     *
     * @param module the module whose catalog is wrong
     * @param locale the locale of the catalog
     * @param problem what is wrong with it
     * @return the violation
     */
    static CatalogViolation ofCatalog(String module, Locale locale, String problem) {
        return new CatalogViolation(module, locale, Optional.empty(), problem);
    }

    /**
     * Describes the violation for a developer, starting with the catalog's path below the classpath root.
     *
     * @return for example {@code i18n/orders/messages_es.properties: key 'orders.placed' is missing (present in en, pt);
     *     ...}
     */
    String describe() {
        var path = "i18n/%s/messages_%s.properties"
                .formatted(module, locale.toLanguageTag().replace('-', '_'));
        return key.map(offending -> "%s: key '%s' %s".formatted(path, offending, problem))
                .orElseGet(() -> "%s: %s".formatted(path, problem));
    }
}
