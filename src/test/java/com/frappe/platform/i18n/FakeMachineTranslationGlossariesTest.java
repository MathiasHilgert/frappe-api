package com.frappe.platform.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class FakeMachineTranslationGlossariesTest {

    @Test
    void returnsTheSameReferenceForTheSameBusinessAndPair() {
        // Given
        var glossaries = new FakeMachineTranslationGlossaries();
        var businessId = UUID.randomUUID();

        // When
        var first = glossaries.ensure(
                businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of("hola", "olá"));
        var second = glossaries.ensure(
                businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of("hola", "olá"));

        // Then
        assertThat(second).isEqualTo(first);
    }

    @Test
    void returnsADifferentReferenceForADifferentPair() {
        // Given
        var glossaries = new FakeMachineTranslationGlossaries();
        var businessId = UUID.randomUUID();

        // When
        var esToPt = glossaries.ensure(businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of());
        var enToPt = glossaries.ensure(businessId, SupportedLocales.ENGLISH, SupportedLocales.PORTUGUESE, Map.of());

        // Then
        assertThat(enToPt).isNotEqualTo(esToPt);
    }
}
