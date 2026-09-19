package com.frappe.platform.infrastructure.i18n;

import static com.github.tomakehurst.wiremock.client.WireMock.aResponse;
import static com.github.tomakehurst.wiremock.client.WireMock.get;
import static com.github.tomakehurst.wiremock.client.WireMock.post;
import static com.github.tomakehurst.wiremock.client.WireMock.postRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.urlEqualTo;
import static org.assertj.core.api.Assertions.assertThat;

import com.deepl.api.DeepLClient;
import com.deepl.api.DeepLClientOptions;
import com.deepl.api.GlossaryEntries;
import com.frappe.platform.i18n.SupportedLocales;
import com.github.tomakehurst.wiremock.junit5.WireMockExtension;
import java.time.Duration;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.RegisterExtension;

/** {@link DeepLGlossaries} against a stub DeepL API; never calls the real DeepL API. */
class DeepLGlossariesTest {

    @RegisterExtension
    static WireMockExtension deepL = WireMockExtension.newInstance().build();

    private DeepLGlossaries glossaries() {
        var client = new DeepLClient(
                "test-key:fx",
                new DeepLClientOptions()
                        .setServerUrl(deepL.baseUrl())
                        .setMaxRetries(0)
                        .setTimeout(Duration.ofSeconds(2)));
        return new DeepLGlossaries(client);
    }

    @Test
    void createsAGlossaryWhenTheBusinessHasNoneYet() {
        // Given
        var businessId = UUID.randomUUID();
        deepL.stubFor(get("/v3/glossaries")
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("{\"glossaries\":[]}")));
        deepL.stubFor(post("/v3/glossaries")
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("""
                                {"glossary_id":"glossary-1","name":"%s","creation_time":"2024-01-01T00:00:00Z",
                                 "dictionaries":[{"source_lang":"es","target_lang":"pt","entry_count":1}]}
                                """.formatted(businessId))));

        // When
        var id = glossaries()
                .ensure(
                        businessId,
                        SupportedLocales.SPANISH,
                        SupportedLocales.PORTUGUESE,
                        new GlossaryEntries(Map.of("hola", "olá")));

        // Then
        assertThat(id).isEqualTo("glossary-1");
        deepL.verify(postRequestedFor(urlEqualTo("/v3/glossaries")));
    }

    @Test
    void reusesAnExistingGlossaryForTheSameBusinessAndLanguagePair() {
        // Given
        var businessId = UUID.randomUUID();
        deepL.stubFor(get("/v3/glossaries")
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("""
                                {"glossaries":[{"glossary_id":"glossary-existing","name":"%s",
                                  "creation_time":"2024-01-01T00:00:00Z",
                                  "dictionaries":[{"source_lang":"es","target_lang":"pt","entry_count":1}]}]}
                                """.formatted(businessId))));

        // When
        var id = glossaries()
                .ensure(
                        businessId,
                        SupportedLocales.SPANISH,
                        SupportedLocales.PORTUGUESE,
                        new GlossaryEntries(Map.of("hola", "olá")));

        // Then
        assertThat(id).isEqualTo("glossary-existing");
        deepL.verify(0, postRequestedFor(urlEqualTo("/v3/glossaries")));
    }
}
