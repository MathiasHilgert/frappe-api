package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import com.frappe.platform.i18n.SupportedLocales;
import java.util.Locale;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.springframework.context.NoSuchMessageException;

class IcuMessageSourceTest {

    private static final String SAMPLE_CATALOGS = "classpath*:i18n/sample/messages_*.properties";

    private final IcuMessageSource messages = IcuMessageSource.load(SAMPLE_CATALOGS);

    @ParameterizedTest
    @CsvSource({
        "en, 1, 1 item",
        "en, 2, 2 items",
        "es, 1, 1 artículo",
        "es, 2, 2 artículos",
        "pt, 1, 1 item",
        "pt, 2, 2 itens"
    })
    void resolvesThePluralFormOfEachLanguage(String language, int count, String expected) {
        // When
        var message = messages.getMessage("sample.items", new Object[] {count}, Locale.forLanguageTag(language));

        // Then
        assertThat(message).isEqualTo(expected);
    }

    @Test
    void resolvesARegionalLocaleFromItsLanguageCatalog() {
        // When / Then
        assertThat(messages.getMessage("sample.greeting", new Object[] {"Ana"}, Locale.forLanguageTag("es-AR")))
                .isEqualTo("¡Hola, Ana!");
    }

    @Test
    void resolvesAnUnsupportedOrMissingLocaleInEnglish() {
        // When / Then
        assertThat(messages.getMessage("sample.greeting", new Object[] {"Ana"}, Locale.FRENCH))
                .isEqualTo("Hello, Ana!");
        assertThat(messages.getMessage("sample.greeting", new Object[] {"Ana"}, null))
                .isEqualTo("Hello, Ana!");
    }

    @Test
    void formatsMessagesWithoutArgumentsThroughIcuToo() {
        // When / Then
        assertThat(messages.getMessage("sample.quoted", null, SupportedLocales.PORTUGUESE))
                .isEqualTo("Use {chaves} para os argumentos");
    }

    @Test
    void pseudoLocalizesTheEnglishCatalogForEnXa() {
        // When / Then
        assertThat(messages.getMessage("sample.greeting", new Object[] {"Ana"}, SupportedLocales.PSEUDO))
                .isEqualTo("[Ĥéĺĺó, Ana!]");
        assertThat(messages.getMessage("sample.items", new Object[] {2}, SupportedLocales.PSEUDO))
                .isEqualTo("[2 íŧéɱš]");
    }

    @Test
    void anUnknownCodeIsNotFound() {
        // When / Then
        assertThatExceptionOfType(NoSuchMessageException.class)
                .isThrownBy(() -> messages.getMessage("sample.unknown", null, SupportedLocales.SPANISH));
        assertThat(messages.getMessage("sample.unknown", null, "fallback", SupportedLocales.SPANISH))
                .isEqualTo("fallback");
    }
}
