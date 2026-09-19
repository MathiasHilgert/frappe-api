package com.frappe.platform.i18n;

import java.util.Locale;

/**
 * Resolves user-facing text from the module catalogs ({@code i18n/<module>/messages_{en,es,pt}.properties}); the one
 * localization type modules depend on. Arguments fill the message's numbered placeholders ({@code {0}}, {@code {1}}),
 * including ICU plural and select.
 *
 * <pre>{@code
 * messages.get("order.items", 2);                          // "2 items" / "2 artículos" / "2 itens"
 * messages.get(recipientLocale, "order.receipt.subject");  // outside a request
 * }</pre>
 *
 * <p>An unknown key is a programming error and fails with an unchecked exception naming the key and the locale.
 */
public interface Messages {

    /**
     * Resolves a message in the locale of the current request (the one the locale chain resolved), or English outside
     * a request.
     *
     * @param key the message key, for example {@code order.items}
     * @param args the values of the numbered placeholders, in order
     * @return the formatted message
     */
    String get(String key, Object... args);

    /**
     * Resolves a message in a given locale, for text produced outside a request (listeners, jobs, emails).
     *
     * @param locale the locale to resolve in; mapped to its supported language, English when none matches
     * @param key the message key, for example {@code order.receipt.subject}
     * @param args the values of the numbered placeholders, in order
     * @return the formatted message
     */
    String get(Locale locale, String key, Object... args);
}
