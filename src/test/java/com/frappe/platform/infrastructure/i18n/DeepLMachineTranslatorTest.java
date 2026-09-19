package com.frappe.platform.infrastructure.i18n;

import static com.github.tomakehurst.wiremock.client.WireMock.aResponse;
import static com.github.tomakehurst.wiremock.client.WireMock.containing;
import static com.github.tomakehurst.wiremock.client.WireMock.equalTo;
import static com.github.tomakehurst.wiremock.client.WireMock.post;
import static com.github.tomakehurst.wiremock.client.WireMock.postRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.urlEqualTo;
import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.i18n.MachineTranslationQuotaExceededException;
import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.i18n.TranslationFormality;
import com.frappe.platform.i18n.TranslationRequest;
import com.github.tomakehurst.wiremock.junit5.WireMockExtension;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.RegisterExtension;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

/**
 * The DeepL adapter's contract against a stub DeepL API, built the same way production wires it
 * ({@link DeepLTranslationConfiguration#machineTranslator}: {@code maxRetries(0)}, the configured timeout, the
 * WireMock server URL). Never calls the real DeepL API.
 */
class DeepLMachineTranslatorTest {

    @RegisterExtension
    static WireMockExtension deepL = WireMockExtension.newInstance().build();

    private final SimpleMeterRegistry meters = new SimpleMeterRegistry();

    private final TestObservationRegistry observations = TestObservationRegistry.create();

    private final DeepLTranslationConfiguration configuration = new DeepLTranslationConfiguration();

    private DeepLMachineTranslator translator() {
        return translator(Map.of("en", "en-US", "pt", "pt-BR"));
    }

    private DeepLMachineTranslator translator(Map<String, String> defaultVariants) {
        var properties =
                new DeepLTranslationProperties("test-key:fx", deepL.baseUrl(), Duration.ofMillis(500), defaultVariants);
        return (DeepLMachineTranslator) configuration.machineTranslator(properties, meters, observations);
    }

    private static void stubTranslate(int status, String body) {
        deepL.stubFor(post("/v2/translate")
                .willReturn(aResponse()
                        .withStatus(status)
                        .withHeader("Content-Type", "application/json")
                        .withBody(body)));
    }

    @Test
    void translatesEachTextAndReportsTheDetectedSourceLanguage() {
        // Given
        stubTranslate(200, """
                {"translations":[
                  {"detected_source_language":"ES","text":"Olá","billed_characters":4},
                  {"detected_source_language":"ES","text":"Adeus","billed_characters":5}
                ]}
                """);

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
        deepL.verify(postRequestedFor(urlEqualTo("/v2/translate"))
                .withHeader("Authorization", equalTo("DeepL-Auth-Key test-key:fx"))
                .withRequestBody(containing("source_lang=es"))
                .withRequestBody(containing("target_lang=pt-BR")));
    }

    @Test
    void mapsAnUnrecognizedDetectedLanguageToEnglish() {
        // Given
        stubTranslate(200, """
                {"translations":[{"detected_source_language":"JA","text":"Hi","billed_characters":2}]}
                """);

        // When
        var result = translator()
                .translate(new TranslationRequest(
                        List.of("こんにちは"), null, SupportedLocales.ENGLISH, null, TranslationFormality.DEFAULT));

        // Then
        assertThat(result.get(0).detectedSourceLanguage()).isEqualTo(SupportedLocales.ENGLISH);
    }

    @Test
    void isAvailableWhenAKeyIsConfigured() {
        assertThat(translator().isAvailable()).isTrue();
    }

    @Test
    void sourceLanguageIsSentBareEvenWhenGivenWithARegion() {
        // Given
        stubTranslate(200, """
                {"translations":[{"detected_source_language":"PT","text":"Hi","billed_characters":2}]}
                """);

        // When
        translator()
                .translate(new TranslationRequest(
                        List.of("Oi"),
                        Locale.forLanguageTag("pt-BR"),
                        SupportedLocales.ENGLISH,
                        null,
                        TranslationFormality.DEFAULT));

        // Then
        deepL.verify(postRequestedFor(urlEqualTo("/v2/translate")).withRequestBody(containing("source_lang=pt")));
    }

    @ParameterizedTest
    @CsvSource({
        "en, , en-US", // bare English: needs the configured default variant
        "pt, , pt-BR", // bare Portuguese: needs the configured default variant
        "es, , es", // bare Spanish: DeepL accepts it bare, no default needed
        "en-GB, en-GB, en-GB", // explicit DeepL-supported variant: sent as-is
        "pt-PT, pt-PT, pt-PT", // explicit DeepL-supported variant: sent as-is
        "es-419, es-419, es-419", // explicit DeepL-supported variant: sent as-is
        "es-AR, es-AR, es" // a region DeepL does not support as a variant: reduced to bare
    })
    void mapsTheTargetLanguageToTheVariantDeepLAccepts(String targetTag, String ignored, String expectedTargetLang) {
        // Given
        stubTranslate(200, """
                {"translations":[{"detected_source_language":"ES","text":"Hi","billed_characters":2}]}
                """);

        // When
        translator(Map.of("en", "en-US", "pt", "pt-BR"))
                .translate(new TranslationRequest(
                        List.of("Hola"),
                        SupportedLocales.SPANISH,
                        Locale.forLanguageTag(targetTag),
                        null,
                        TranslationFormality.DEFAULT));

        // Then
        deepL.verify(postRequestedFor(urlEqualTo("/v2/translate"))
                .withRequestBody(containing("target_lang=" + expectedTargetLang)));
    }

    @Test
    void forwardsAGlossaryReference() {
        // Given
        stubTranslate(200, """
                {"translations":[{"detected_source_language":"ES","text":"Olá","billed_characters":4}]}
                """);

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
    void quotaExceededMapsToADedicatedExceptionAndTagsTheOutcome() {
        // Given
        stubTranslate(456, """
                {"message":"Quota for this billing period has been exceeded"}
                """);

        // When / Then
        assertThatThrownBy(() -> translator().translate(request()))
                .isInstanceOf(MachineTranslationQuotaExceededException.class);
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasLowCardinalityKeyValue("outcome", "quota_exceeded");
        assertThat(meters.find(DeepLMachineTranslator.CHARACTERS_METRIC).counter())
                .isNull();
        deepL.verify(1, postRequestedFor(urlEqualTo("/v2/translate")));
    }

    @Test
    void anInvalidKeyMapsToUnavailableAndTagsInvalidKey() {
        // Given
        stubTranslate(403, """
                {"message":"Authorization failure, check auth_key"}
                """);

        // When / Then
        assertThatThrownBy(() -> translator().translate(request()))
                .isInstanceOf(MachineTranslationUnavailableException.class);
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasLowCardinalityKeyValue("outcome", "invalid_key");
        deepL.verify(1, postRequestedFor(urlEqualTo("/v2/translate")));
    }

    @Test
    void aTooManyRequestsResponseMapsToUnavailableRateLimitedWithinOneRequest() {
        // Given
        stubTranslate(429, """
                {"message":"Too many requests"}
                """);

        // When / Then
        assertThatThrownBy(() -> translator().translate(request()))
                .isInstanceOf(MachineTranslationUnavailableException.class)
                .hasMessageContaining("rate limited");
        deepL.verify(1, postRequestedFor(urlEqualTo("/v2/translate")));
    }

    @Test
    void aTimeoutMapsToUnavailableWithinTheBoundedTimeoutAndExactlyOneRequest() {
        // Given a server that never answers within the client's timeout (500ms)
        deepL.stubFor(post("/v2/translate")
                .willReturn(aResponse().withFixedDelay(3000).withStatus(200)));
        var start = Instant.now();

        // When / Then
        assertThatThrownBy(() -> translator().translate(request()))
                .isInstanceOf(MachineTranslationUnavailableException.class);
        assertThat(Duration.between(start, Instant.now())).isLessThan(Duration.ofSeconds(2));
        deepL.verify(1, postRequestedFor(urlEqualTo("/v2/translate")));
    }

    @Test
    void isUnavailableWithoutAnyRequestWhenNoKeyIsConfigured() {
        // Given
        var noKey = new DeepLMachineTranslator(meters, observations);

        // When / Then
        assertThat(noKey.isAvailable()).isFalse();
        assertThatThrownBy(() -> noKey.translate(request())).isInstanceOf(MachineTranslationUnavailableException.class);
        deepL.verify(0, postRequestedFor(urlEqualTo("/v2/translate")));
    }

    @Test
    void recordsBilledCharactersOnlyOnSuccess() {
        // Given
        stubTranslate(200, """
                {"translations":[{"detected_source_language":"ES","text":"Olá","billed_characters":7}]}
                """);

        // When
        translator().translate(request());

        // Then
        assertThat(meters.find(DeepLMachineTranslator.CHARACTERS_METRIC)
                        .tag(DeepLMachineTranslator.TARGET_LANGUAGE_TAG, "pt")
                        .counter()
                        .count())
                .isEqualTo(7.0);
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasLowCardinalityKeyValue("outcome", "success")
                .hasLowCardinalityKeyValue("target_language", "pt")
                .hasBeenStopped();
    }

    private static TranslationRequest request() {
        return new TranslationRequest(
                List.of("Hola"),
                SupportedLocales.SPANISH,
                SupportedLocales.PORTUGUESE,
                null,
                TranslationFormality.DEFAULT);
    }
}
