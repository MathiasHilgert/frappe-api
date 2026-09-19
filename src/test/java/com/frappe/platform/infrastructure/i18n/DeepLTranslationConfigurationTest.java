package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.observation.ObservationRegistry;
import java.time.Duration;
import org.junit.jupiter.api.Test;

class DeepLTranslationConfigurationTest {

    private final DeepLTranslationConfiguration configuration = new DeepLTranslationConfiguration();

    @Test
    void reportsUnavailableWithoutFailingWhenNoKeyIsConfigured() {
        // Given
        var properties = new DeepLTranslationProperties("", null, Duration.ofSeconds(10));

        // When
        var translator =
                configuration.machineTranslator(properties, new SimpleMeterRegistry(), ObservationRegistry.create());

        // Then
        assertThat(translator.isAvailable()).isFalse();
    }

    @Test
    void isAvailableWhenAKeyIsConfigured() {
        // Given
        var properties = new DeepLTranslationProperties("a-key:fx", "http://localhost:1", Duration.ofSeconds(10));

        // When
        var translator =
                configuration.machineTranslator(properties, new SimpleMeterRegistry(), ObservationRegistry.create());

        // Then
        assertThat(translator.isAvailable()).isTrue();
    }
}
