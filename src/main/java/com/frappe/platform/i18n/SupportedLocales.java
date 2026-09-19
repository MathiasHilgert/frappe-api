package com.frappe.platform.i18n;

import com.ibm.icu.util.LocaleMatcher;
import com.ibm.icu.util.LocalePriorityList;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Optional;
import java.util.SequencedSet;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * The languages Frappé speaks (English, Spanish, Portuguese) and the CLDR-based matching of what a client or tenant
 * asks for onto them, so that regional variants such as {@code es-AR} or {@code pt-BR} resolve to their language.
 *
 * <p>Instances are immutable and thread-safe. {@link #all()} holds every supported locale; {@link #restrictedTo}
 * narrows matching to the languages a business enabled.
 */
public final class SupportedLocales {

    /** English, the language every resolution falls back to. */
    public static final Locale ENGLISH = Locale.of("en");

    /** Spanish. */
    public static final Locale SPANISH = Locale.of("es");

    /** Portuguese. */
    public static final Locale PORTUGUESE = Locale.of("pt");

    /** The locale used when nothing else matches: every response has exactly one language. */
    public static final Locale FALLBACK = ENGLISH;

    private static final Logger log = LoggerFactory.getLogger(SupportedLocales.class);

    private static final SupportedLocales ALL = new SupportedLocales(List.of(ENGLISH, SPANISH, PORTUGUESE));

    private final SequencedSet<Locale> locales;
    private final LocaleMatcher matcher;

    private SupportedLocales(Collection<Locale> locales) {
        this.locales = Collections.unmodifiableSequencedSet(new LinkedHashSet<>(locales));
        // Without a default locale the matcher answers "no match" instead of silently picking the first supported
        // locale, so the caller's chain decides what comes next.
        this.matcher = LocaleMatcher.builder()
                .setSupportedLocales(this.locales)
                .setNoDefaultLocale()
                .build();
    }

    /**
     * Returns every supported locale: English, Spanish and Portuguese, in that order.
     *
     * @return the supported locales
     */
    public static SupportedLocales all() {
        return ALL;
    }

    /**
     * Narrows matching to the given languages, for a business that enabled only some of them. Languages that are not
     * supported are ignored; if none is supported, nothing matches.
     *
     * @param enabled the languages to keep, compared by equality with the supported locales
     * @return the supported locales that are also in {@code enabled}, in supported order
     */
    public SupportedLocales restrictedTo(Collection<Locale> enabled) {
        return new SupportedLocales(locales.stream().filter(enabled::contains).toList());
    }

    /**
     * Returns the locales matching considers.
     *
     * @return the locales, in supported order
     */
    public SequencedSet<Locale> locales() {
        return locales;
    }

    /**
     * Matches a single desired locale, using CLDR language distance (so {@code es-AR} matches {@code es}).
     *
     * @param desired the locale asked for
     * @return the supported locale it matches, or empty when no supported locale is close enough
     */
    public Optional<Locale> match(Locale desired) {
        return Optional.ofNullable(matcher.getBestLocaleResult(desired).getSupportedLocale());
    }

    /**
     * Matches the value of an {@code Accept-Language} header, honouring its quality weights ({@code q=0} excludes a
     * language).
     *
     * @param header the header value, for example {@code es-AR,es;q=0.9,en;q=0.5}
     * @return the supported locale that best matches, or empty when none matches or the header is malformed
     */
    public Optional<Locale> matchAcceptLanguage(String header) {
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
