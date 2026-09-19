package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import com.frappe.platform.i18n.SupportedLocales;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.observation.ObservationRegistry;
import java.time.Duration;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class DeepLTranslationConfigurationTest {

    private final DeepLTranslationConfiguration configuration = new DeepLTranslationConfiguration();

    @Test
    void translatorReportsUnavailableWithoutFailingWhenNoKeyIsConfigured() {
        // Given
        var properties = new DeepLTranslationProperties("", null, Duration.ofSeconds(10), Map.of());

        // When
        var translator =
                configuration.machineTranslator(properties, new SimpleMeterRegistry(), ObservationRegistry.create());

        // Then
        assertThat(translator.isAvailable()).isFalse();
    }

    @Test
    void translatorIsAvailableWhenAKeyIsConfigured() {
        // Given
        var properties =
                new DeepLTranslationProperties("a-key:fx", "http://localhost:1", Duration.ofSeconds(10), Map.of());

        // When
        var translator =
                configuration.machineTranslator(properties, new SimpleMeterRegistry(), ObservationRegistry.create());

        // Then
        assertThat(translator.isAvailable()).isTrue();
    }

    @Test
    void glossariesFailFastWithoutFailingStartupWhenNoKeyIsConfigured() {
        // Given
        var properties = new DeepLTranslationProperties("", null, Duration.ofSeconds(10), Map.of());
        var glossaries = configuration.machineTranslationGlossaries(properties);

        // When / Then
        assertThatThrownBy(() -> glossaries.ensure(
                        UUID.randomUUID(), SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of()))
                .isInstanceOf(MachineTranslationUnavailableException.class);
    }

    @Test
    void glossariesAreBackedByDeepLWhenAKeyIsConfigured() {
        // Given
        var properties =
                new DeepLTranslationProperties("a-key:fx", "http://localhost:1", Duration.ofSeconds(10), Map.of());

        // When
        var glossaries = configuration.machineTranslationGlossaries(properties);

        // Then
        assertThat(glossaries).isInstanceOf(DeepLGlossaries.class);
    }
}
