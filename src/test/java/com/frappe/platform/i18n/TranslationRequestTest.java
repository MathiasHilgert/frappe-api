package com.frappe.platform.i18n;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.util.List;
import org.junit.jupiter.api.Test;

class TranslationRequestTest {

    @Test
    void ofCreatesARequestWithoutGlossaryAndDefaultFormality() {
        // Given / When
        var request = TranslationRequest.of(List.of("Hola"), SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE);

        // Then
        assertThat(request.texts()).containsExactly("Hola");
        assertThat(request.glossaryReference()).isNull();
        assertThat(request.formality()).isEqualTo(TranslationFormality.DEFAULT);
    }

    @Test
    void rejectsAnEmptyTextList() {
        assertThatThrownBy(() -> new TranslationRequest(
                        List.of(),
                        SupportedLocales.SPANISH,
                        SupportedLocales.PORTUGUESE,
                        null,
                        TranslationFormality.DEFAULT))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    void rejectsABlankText() {
        assertThatThrownBy(() -> new TranslationRequest(
                        List.of("Hola", "  "),
                        SupportedLocales.SPANISH,
                        SupportedLocales.PORTUGUESE,
                        null,
                        TranslationFormality.DEFAULT))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    void allowsANullSourceLanguageForAutoDetection() {
        // Given / When
        var request = new TranslationRequest(
                List.of("Hola"), null, SupportedLocales.PORTUGUESE, null, TranslationFormality.DEFAULT);

        // Then
        assertThat(request.sourceLanguage()).isNull();
    }

    @Test
    void rejectsANullTargetLanguage() {
        assertThatThrownBy(() -> new TranslationRequest(
                        List.of("Hola"), SupportedLocales.SPANISH, null, null, TranslationFormality.DEFAULT))
                .isInstanceOf(NullPointerException.class);
    }
}
