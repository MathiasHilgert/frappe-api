package com.frappe.platform.i18n;

import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.SequencedSet;

/**
 * The languages Frappé speaks: English, Spanish and Portuguese. Every response is in exactly one of them; regional
 * variants ({@code es-AR}, {@code pt-BR}) resolve to their language.
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

    /**
     * The pseudo-locale en-XA, for tests only: messages resolve from the English catalog accented and bracketed
     * ({@code Hello, {0}!} becomes {@code [Ĥéĺĺó, {0}!]}), which proves text comes from a catalog. Never served to
     * clients.
     */
    public static final Locale PSEUDO = Locale.forLanguageTag("en-XA");

    private static final SequencedSet<Locale> ALL =
            Collections.unmodifiableSequencedSet(new LinkedHashSet<>(List.of(ENGLISH, SPANISH, PORTUGUESE)));

    private SupportedLocales() {}

    /**
     * Returns every supported locale.
     *
     * @return English, Spanish and Portuguese, in that order
     */
    public static SequencedSet<Locale> all() {
        return ALL;
    }
}
