package com.frappe.platform.infrastructure.i18n;

import com.frappe.platform.i18n.SupportedLocales;
import com.ibm.icu.text.MessageFormat;
import com.ibm.icu.util.ULocale;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.jspecify.annotations.Nullable;
import org.springframework.context.support.AbstractMessageSource;

/**
 * The application {@code MessageSource}: resolves user-facing text from the per-module catalogs
 * {@code i18n/<module>/messages_{en,es,pt}.properties} and formats it with ICU MessageFormat, so messages can use
 * plural and select (which {@code java.text.MessageFormat}, behind Spring's own message sources, lacks).
 *
 * <p>Arguments are numbered ({@code {0}}), because {@code MessageSource} passes them as an array. A locale is mapped to
 * its supported language ({@code es-AR} reads the Spanish catalog); an unsupported or missing locale reads the English
 * one. {@link SupportedLocales#PSEUDO} (en-XA) reads the English catalog pseudo-localized. Every message is formatted by ICU,
 * with or without arguments, so quoting behaves the same everywhere. Codes the catalogs do not know fall through to
 * Spring's common messages and parent message source.
 */
final class IcuMessageSource extends AbstractMessageSource {

    /** Where the application's catalogs live: one directory per module below {@code i18n}. */
    static final String CATALOG_LOCATIONS = "classpath*:i18n/*/messages_*.properties";

    private final Map<Locale, Map<String, String>> patternsByLocale;

    private IcuMessageSource(Map<Locale, Map<String, String>> patternsByLocale) {
        this.patternsByLocale = Map.copyOf(patternsByLocale);
    }

    /**
     * Loads the catalogs matching a location pattern.
     *
     * @param locationPattern a Spring resource pattern, usually {@link #CATALOG_LOCATIONS}
     * @return the message source over those catalogs
     * @throws UnreadableMessageCatalogException if a catalog cannot be read
     * @throws InvalidMessageCatalogsException if the catalogs are incomplete or contain invalid messages, listing every
     *     violation
     */
    static IcuMessageSource load(String locationPattern) {
        var catalogs = MessageCatalogs.load(locationPattern);
        var violations = MessageCatalogCheck.violations(catalogs);
        if (!violations.isEmpty()) {
            throw new InvalidMessageCatalogsException(violations);
        }
        return new IcuMessageSource(patternsByLocale(catalogs));
    }

    private static Map<Locale, Map<String, String>> patternsByLocale(List<MessageCatalog> catalogs) {
        var patterns = new HashMap<Locale, Map<String, String>>();
        SupportedLocales.all().forEach(locale -> patterns.put(locale, new HashMap<>()));
        catalogs.stream()
                .filter(catalog -> patterns.containsKey(catalog.locale()))
                .forEach(catalog -> patterns.get(catalog.locale()).putAll(catalog.messages()));
        var pseudo = new HashMap<String, String>();
        patterns.get(SupportedLocales.FALLBACK)
                .forEach((code, pattern) -> pseudo.put(code, PseudoLocalization.of(pattern)));
        patterns.put(SupportedLocales.PSEUDO, pseudo);
        patterns.replaceAll((locale, byCode) -> Map.copyOf(byCode));
        return patterns;
    }

    @Override
    protected @Nullable String getMessageInternal(
            @Nullable String code, Object @Nullable [] args, @Nullable Locale locale) {
        if (code == null) {
            return null;
        }
        var catalogLocale = catalogLocaleOf(locale);
        var pattern = patternsByLocale.get(catalogLocale).get(code);
        if (pattern == null) {
            return super.getMessageInternal(code, args, catalogLocale);
        }
        // ICU MessageFormat instances are not thread-safe: a fresh one per message avoids sharing mutable state, at the
        // cost of parsing one short pattern.
        return new MessageFormat(pattern, ULocale.forLocale(catalogLocale))
                .format(resolveArguments(args, catalogLocale));
    }

    /**
     * Never used: every catalog message is formatted by ICU in {@link #getMessageInternal}, and Spring's
     * {@code java.text.MessageFormat} path would lose plural and select.
     *
     * @param code the message code
     * @param locale the locale
     * @return always {@code null}
     */
    @Override
    protected java.text.@Nullable MessageFormat resolveCode(String code, Locale locale) {
        return null;
    }

    private static Locale catalogLocaleOf(@Nullable Locale locale) {
        if (locale == null) {
            return SupportedLocales.FALLBACK;
        }
        if (SupportedLocales.PSEUDO.equals(locale)) {
            return SupportedLocales.PSEUDO;
        }
        return LocaleMatching.all().match(locale).orElse(SupportedLocales.FALLBACK);
    }
}
