package com.frappe.platform.infrastructure.i18n;

import com.frappe.platform.i18n.SupportedLocales;
import com.ibm.icu.util.LocaleMatcher;
import com.ibm.icu.util.LocalePriorityList;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Locale;
import java.util.Optional;
import java.util.SequencedSet;
import java.util.stream.Collectors;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * CLDR-based matching (ICU4J {@code LocaleMatcher}) of what a client or tenant asks for onto the
 * {@linkplain SupportedLocales supported locales}, so that regional variants such as {@code es-AR} or {@code pt-BR}
 * resolve to their language.
 *
 * <p>Instances are immutable and thread-safe. {@link #all()} matches every supported locale; {@link #restrictedTo}
 * narrows matching to the languages a business enabled.
 */
final class LocaleMatching {

    private static final Logger log = LoggerFactory.getLogger(LocaleMatching.class);

    private static final LocaleMatching ALL = new LocaleMatching(SupportedLocales.all());

    private final SequencedSet<Locale> locales;
    private final LocaleMatcher matcher;

    private LocaleMatching(Collection<Locale> locales) {
        this.locales = Collections.unmodifiableSequencedSet(new LinkedHashSet<>(locales));
        // Without a default locale the matcher answers "no match" instead of silently picking the first supported
        // locale, so the caller's chain decides what comes next.
        this.matcher = LocaleMatcher.builder()
                .setSupportedLocales(this.locales)
                .setNoDefaultLocale()
                .build();
    }

    /**
     * Returns matching over every supported locale.
     *
     * @return matching over English, Spanish and Portuguese
     */
    static LocaleMatching all() {
        return ALL;
    }

    /**
     * Narrows matching to the given languages, for a business that enabled only some of them. Each enabled locale is
     * matched like any other ({@code pt-BR} enables {@code pt}); locales that match nothing are ignored, and if none
     * matches, nothing does.
     *
     * @param enabled the languages to keep
     * @return the supported locales the enabled ones match, in supported order
     */
    LocaleMatching restrictedTo(Collection<Locale> enabled) {
        var matched = enabled.stream().flatMap(locale -> match(locale).stream()).collect(Collectors.toSet());
        return new LocaleMatching(locales.stream().filter(matched::contains).toList());
    }

    /**
     * Returns the locales matching considers.
     *
     * @return the locales, in supported order
     */
    SequencedSet<Locale> locales() {
        return locales;
    }

    /**
     * Matches a single desired locale, using CLDR language distance (so {@code es-AR} matches {@code es}).
     *
     * @param desired the locale asked for
     * @return the supported locale it matches, or empty when no supported locale is close enough
     */
    Optional<Locale> match(Locale desired) {
        return Optional.ofNullable(matcher.getBestLocaleResult(desired).getSupportedLocale());
    }

    /**
     * Matches the value of an {@code Accept-Language} header, honouring its quality weights ({@code q=0} excludes a
     * language).
     *
     * @param header the header value, for example {@code es-AR,es;q=0.9,en;q=0.5}
     * @return the supported locale that best matches, or empty when none matches or the header is malformed
     */
    Optional<Locale> matchAcceptLanguage(String header) {
        return parse(header)
                .flatMap(desired ->
                        Optional.ofNullable(matcher.getBestMatchResult(desired).getSupportedLocale()));
    }

    private static Optional<LocalePriorityList> parse(String header) {
        try {
            return Optional.of(LocalePriorityList.add(header).build());
        } catch (IllegalArgumentException malformed) {
            // A client sent a weight outside 0..1 or not a number; the request continues as if it named no language.
            log.atDebug()
                    .addKeyValue(LogFields.ACCEPT_LANGUAGE, header)
                    .setCause(malformed)
                    .log("Ignoring a malformed Accept-Language header");
            return Optional.empty();
        }
    }
}
