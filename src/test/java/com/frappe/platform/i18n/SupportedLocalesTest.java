package com.frappe.platform.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import java.util.Locale;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.ValueSource;

class SupportedLocalesTest {

    @Test
    void supportsEnglishSpanishAndPortugueseWithEnglishAsTheFallback() {
        // When / Then
        assertThat(SupportedLocales.all().locales())
                .containsExactly(Locale.forLanguageTag("en"), Locale.forLanguageTag("es"), Locale.forLanguageTag("pt"));
        assertThat(SupportedLocales.FALLBACK).isEqualTo(Locale.forLanguageTag("en"));
    }

    @ParameterizedTest
    // ca and gl match es through CLDR language distance (their closest supported language); accepted, see i18n.md.
    @CsvSource({"es-AR, es", "es-419, es", "pt-BR, pt", "pt-PT, pt", "en-GB, en", "es, es", "ca, es", "gl, es"})
    void matchesRegionalVariantsToTheirSupportedLanguage(String desired, String expected) {
        // When
        var match = SupportedLocales.all().match(Locale.forLanguageTag(desired));

        // Then
        assertThat(match).contains(Locale.forLanguageTag(expected));
    }

    @ParameterizedTest
    @ValueSource(strings = {"fr", "it", "und", "en-XA"})
    void matchesNothingForUnsupportedLanguages(String desired) {
        // When / Then
        assertThat(SupportedLocales.all().match(Locale.forLanguageTag(desired))).isEmpty();
    }

    @ParameterizedTest
    @CsvSource(
            delimiter = '|',
            value = {
                "es-AR | es",
                "pt-BR | pt",
                "fr;q=1, pt;q=0.5 | pt",
                "es-AR;q=0.8, en;q=0.9 | en",
                "de, es-419;q=0.3 | es"
            })
    void matchesTheBestWeightedLanguageOfAnAcceptLanguageHeader(String header, String expected) {
        // When
        var match = SupportedLocales.all().matchAcceptLanguage(header);

        // Then
        assertThat(match).contains(Locale.forLanguageTag(expected));
    }

    @ParameterizedTest
    @ValueSource(strings = {"", " ", "fr", "*", "fr, es;q=0", "en;q=2", "es;q=abc"})
    void matchesNothingForAnUnsupportedEmptyOrMalformedAcceptLanguageHeader(String header) {
        // When / Then
        assertThat(SupportedLocales.all().matchAcceptLanguage(header)).isEmpty();
    }

    @Test
    void restrictedToEnabledLanguagesMatchesOnlyThose() {
        // Given
        var enabled = SupportedLocales.all()
                .restrictedTo(List.of(SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Locale.FRENCH));

        // When / Then
        assertThat(enabled.locales()).containsExactly(SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE);
        assertThat(enabled.matchAcceptLanguage("en")).isEmpty();
        assertThat(enabled.match(Locale.forLanguageTag("es-MX"))).contains(SupportedLocales.SPANISH);
    }

    @Test
    void restrictedToRegionalEnabledLanguagesKeepsTheirSupportedLanguage() {
        // Given
        var enabled = SupportedLocales.all()
                .restrictedTo(List.of(Locale.forLanguageTag("pt-BR"), Locale.forLanguageTag("es-AR")));

        // When / Then
        assertThat(enabled.locales()).containsExactly(SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE);
        assertThat(enabled.matchAcceptLanguage("en")).isEmpty();
    }

    @Test
    void restrictedToNoSupportedLanguageMatchesNothing() {
        // Given
        var enabled = SupportedLocales.all().restrictedTo(List.of(Locale.FRENCH));

        // When / Then
        assertThat(enabled.locales()).isEmpty();
        assertThat(enabled.match(SupportedLocales.ENGLISH)).isEmpty();
    }
}
