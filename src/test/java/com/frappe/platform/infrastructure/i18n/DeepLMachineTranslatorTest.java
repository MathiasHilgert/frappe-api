package com.frappe.platform.infrastructure.i18n;

import static com.github.tomakehurst.wiremock.client.WireMock.aResponse;
import static com.github.tomakehurst.wiremock.client.WireMock.containing;
import static com.github.tomakehurst.wiremock.client.WireMock.post;
import static com.github.tomakehurst.wiremock.client.WireMock.postRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.urlEqualTo;
import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.deepl.api.DeepLClient;
import com.deepl.api.DeepLClientOptions;
import com.frappe.platform.i18n.MachineTranslationQuotaExceededException;
import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.i18n.TranslationFormality;
import com.frappe.platform.i18n.TranslationRequest;
import com.github.tomakehurst.wiremock.junit5.WireMockExtension;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.observation.ObservationRegistry;
import java.time.Duration;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.RegisterExtension;

/** The DeepL adapter's contract against a stub DeepL API; never calls the real DeepL API. */
class DeepLMachineTranslatorTest {

    @RegisterExtension
    static WireMockExtension deepL = WireMockExtension.newInstance().build();

    private final SimpleMeterRegistry meters = new SimpleMeterRegistry();

    private DeepLMachineTranslator translator() {
        var client = new DeepLClient(
                "test-key:fx",
                new DeepLClientOptions()
                        .setServerUrl(deepL.baseUrl())
                        .setMaxRetries(0)
                        .setTimeout(Duration.ofSeconds(2)));
        return new DeepLMachineTranslator(client, meters, ObservationRegistry.create());
    }

    @Test
    void translatesEachTextAndReportsTheDetectedSourceLanguage() {
        // Given
        deepL.stubFor(post("/v2/translate")
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("""
                                {"translations":[
                                  {"detected_source_language":"ES","text":"Olá","billed_characters":4},
                                  {"detected_source_language":"ES","text":"Adeus","billed_characters":5}
                                ]}
                                """)));

        // When
        var result = translator()
                .translate(new TranslationRequest(
                        List.of("Hola", "Adiós"),
                        SupportedLocales.SPANISH,
                        SupportedLocales.PORTUGUESE,
                        null,
                        TranslationFormality.DEFAULT));

        // Then
        assertThat(result).hasSize(2);
        assertThat(result.get(0).text()).isEqualTo("Olá");
        assertThat(result.get(0).detectedSourceLanguage()).isEqualTo(SupportedLocales.SPANISH);
        assertThat(result.get(1).text()).isEqualTo("Adeus");
    }

    @Test
    void isAvailableWhenAKeyIsConfigured() {
        assertThat(translator().isAvailable()).isTrue();
    }

    @Test
    void forwardsAGlossaryReference() {
        // Given
        deepL.stubFor(post("/v2/translate")
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("""
                                {"translations":[{"detected_source_language":"ES","text":"Olá","billed_characters":4}]}
                                """)));

        // When
        translator()
                .translate(new TranslationRequest(
                        List.of("Hola"),
                        SupportedLocales.SPANISH,
                        SupportedLocales.PORTUGUESE,
                        "glossary-123",
                        TranslationFormality.DEFAULT));

        // Then
        deepL.verify(
                postRequestedFor(urlEqualTo("/v2/translate")).withRequestBody(containing("glossary_id=glossary-123")));
    }

    @Test
    void quotaExceededMapsToADedicatedException() {
        // Given
        deepL.stubFor(post("/v2/translate").willReturn(aResponse().withStatus(456)));

        // When / Then
        assertThatThrownBy(() -> translator()
                        .translate(new TranslationRequest(
                                List.of("Hola"),
                                SupportedLocales.SPANISH,
                                SupportedLocales.PORTUGUESE,
                                null,
                                TranslationFormality.DEFAULT)))
                .isInstanceOf(MachineTranslationQuotaExceededException.class);
    }

    @Test
    void aTimeoutMapsToUnavailableWithinTheBoundedTimeout() {
        // Given a server that never answers within the client's timeout
        deepL.stubFor(post("/v2/translate")
                .willReturn(aResponse().withFixedDelay(5000).withStatus(200)));

        // When / Then
        assertThatThrownBy(() -> translator()
                        .translate(new TranslationRequest(
                                List.of("Hola"),
                                SupportedLocales.SPANISH,
                                SupportedLocales.PORTUGUESE,
                                null,
                                TranslationFormality.DEFAULT)))
                .isInstanceOf(MachineTranslationUnavailableException.class);
    }

    @Test
    void anInvalidKeyMapsToUnavailable() {
        // Given
        deepL.stubFor(post("/v2/translate").willReturn(aResponse().withStatus(403)));

        // When / Then
        assertThatThrownBy(() -> translator()
                        .translate(new TranslationRequest(
                                List.of("Hola"),
                                SupportedLocales.SPANISH,
                                SupportedLocales.PORTUGUESE,
                                null,
                                TranslationFormality.DEFAULT)))
                .isInstanceOf(MachineTranslationUnavailableException.class);
    }
}
