package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.i18n.TranslationFormality;
import com.frappe.platform.i18n.TranslationRequest;
import java.util.List;
import org.junit.jupiter.api.Test;

class FakeMachineTranslatorTest {

    @Test
    void isAlwaysAvailableByDefault() {
        assertThat(new FakeMachineTranslator().isAvailable()).isTrue();
    }

    @Test
    void translatesDeterministicallyTaggingTheTargetLanguage() {
        // Given
        var translator = new FakeMachineTranslator();

        // When
        var result = translator.translate(new TranslationRequest(
                List.of("Hola"),
                SupportedLocales.SPANISH,
                SupportedLocales.PORTUGUESE,
                null,
                TranslationFormality.DEFAULT));

        // Then
        assertThat(result).hasSize(1);
        assertThat(result.get(0).text()).isEqualTo("[pt] Hola");
        assertThat(result.get(0).detectedSourceLanguage()).isEqualTo(SupportedLocales.SPANISH);
    }

    @Test
    void detectsTheTargetLanguageAsSourceWhenNoneWasGiven() {
        // Given
        var translator = new FakeMachineTranslator();

        // When
        var result = translator.translate(new TranslationRequest(
                List.of("Hola"), null, SupportedLocales.PORTUGUESE, null, TranslationFormality.DEFAULT));

        // Then
        assertThat(result.get(0).detectedSourceLanguage()).isEqualTo(SupportedLocales.ENGLISH);
    }

    @Test
    void aFakeMarkedUnavailableFailsLikeAMissingKey() {
        // Given
        var translator = FakeMachineTranslator.unavailable();

        // Then
        assertThat(translator.isAvailable()).isFalse();
        assertThatThrownBy(() -> translator.translate(new TranslationRequest(
                        List.of("Hola"),
                        SupportedLocales.SPANISH,
                        SupportedLocales.PORTUGUESE,
                        null,
                        TranslationFormality.DEFAULT)))
                .isInstanceOf(MachineTranslationUnavailableException.class);
    }
}
