package com.frappe.platform.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.Locale;
import org.junit.jupiter.api.Test;

class SupportedLocalesTest {

    @Test
    void supportsEnglishSpanishAndPortugueseWithEnglishAsTheFallback() {
        // When / Then
        assertThat(SupportedLocales.all())
                .containsExactly(Locale.forLanguageTag("en"), Locale.forLanguageTag("es"), Locale.forLanguageTag("pt"));
        assertThat(SupportedLocales.FALLBACK).isEqualTo(Locale.forLanguageTag("en"));
    }

    @Test
    void offersEnXaAsThePseudoLocaleForTests() {
        // When / Then
        assertThat(SupportedLocales.PSEUDO).isEqualTo(Locale.forLanguageTag("en-XA"));
        assertThat(SupportedLocales.all()).doesNotContain(SupportedLocales.PSEUDO);
    }
}
