package com.frappe.platform.infrastructure.i18n;

import static com.github.tomakehurst.wiremock.client.WireMock.aResponse;
import static com.github.tomakehurst.wiremock.client.WireMock.delete;
import static com.github.tomakehurst.wiremock.client.WireMock.deleteRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.get;
import static com.github.tomakehurst.wiremock.client.WireMock.getRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.post;
import static com.github.tomakehurst.wiremock.client.WireMock.postRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.put;
import static com.github.tomakehurst.wiremock.client.WireMock.putRequestedFor;
import static com.github.tomakehurst.wiremock.client.WireMock.urlEqualTo;
import static com.github.tomakehurst.wiremock.client.WireMock.urlPathEqualTo;
import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.i18n.SupportedLocales;
import com.github.tomakehurst.wiremock.junit5.WireMockExtension;
import java.time.Duration;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.RegisterExtension;

/**
 * {@link DeepLGlossaries}'s contract against a stub DeepL API, built the same way production wires it
 * ({@link DeepLTranslationConfiguration#machineTranslationGlossaries}). Never calls the real DeepL API.
 */
class DeepLGlossariesTest {

    @RegisterExtension
    static WireMockExtension deepL = WireMockExtension.newInstance().build();

    private DeepLGlossaries glossaries() {
        var properties =
                new DeepLTranslationProperties("test-key:fx", deepL.baseUrl(), Duration.ofSeconds(2), Map.of());
        return (DeepLGlossaries) new DeepLTranslationConfiguration().machineTranslationGlossaries(properties);
    }

    private static void stubEmptyList() {
        deepL.stubFor(get(urlPathEqualTo("/v3/glossaries"))
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("{\"glossaries\":[]}")));
    }

    private static void stubList(String glossariesJson) {
        deepL.stubFor(get(urlPathEqualTo("/v3/glossaries"))
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("{\"glossaries\":[" + glossariesJson + "]}")));
    }

    private static String glossaryJson(String id, String businessName, String creationTime, String dictionariesJson) {
        return """
                {"glossary_id":"%s","name":"%s","creation_time":"%s","dictionaries":[%s]}
                """.formatted(id, businessName, creationTime, dictionariesJson);
    }

    private static String dictionaryJson(String source, String target) {
        return """
                {"source_lang":"%s","target_lang":"%s","entry_count":1}
                """.formatted(source, target);
    }

    @Test
    void createsAGlossaryWhenTheBusinessHasNoneYet() {
        // Given
        var businessId = UUID.randomUUID();
        var name = "frappe-business-" + businessId;
        stubEmptyList();
        deepL.stubFor(post(urlPathEqualTo("/v3/glossaries"))
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody(
                                glossaryJson("glossary-1", name, "2024-01-01T00:00:00Z", dictionaryJson("es", "pt")))));

        // When
        var id = glossaries()
                .ensure(businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of("hola", "olá"));

        // Then
        assertThat(id).isEqualTo("glossary-1");
        deepL.verify(postRequestedFor(urlEqualTo("/v3/glossaries")));
    }

    @Test
    void reusesAnExistingGlossaryForTheSameBusinessAndLanguagePairByReplacingItsDictionary() {
        // Given
        var businessId = UUID.randomUUID();
        var name = "frappe-business-" + businessId;
        stubList(glossaryJson("glossary-existing", name, "2024-01-01T00:00:00Z", dictionaryJson("es", "pt")));
        deepL.stubFor(put(urlPathEqualTo("/v3/glossaries/glossary-existing/dictionaries"))
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody(dictionaryJson("es", "pt"))));

        // When
        var id = glossaries()
                .ensure(businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of("hola", "olá"));

        // Then
        assertThat(id).isEqualTo("glossary-existing");
        deepL.verify(0, postRequestedFor(urlEqualTo("/v3/glossaries")));
        deepL.verify(putRequestedFor(urlPathEqualTo("/v3/glossaries/glossary-existing/dictionaries")));
    }

    @Test
    void addsADictionaryToTheBusinessGlossaryWhenItExistsOnlyForAnotherPair() {
        // Given the business already has a glossary, but only an es->pt dictionary
        var businessId = UUID.randomUUID();
        var name = "frappe-business-" + businessId;
        stubList(glossaryJson("glossary-existing", name, "2024-01-01T00:00:00Z", dictionaryJson("es", "pt")));
        deepL.stubFor(put(urlPathEqualTo("/v3/glossaries/glossary-existing/dictionaries"))
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody(dictionaryJson("en", "pt"))));

        // When requesting the en->pt pair
        var id = glossaries()
                .ensure(businessId, SupportedLocales.ENGLISH, SupportedLocales.PORTUGUESE, Map.of("hi", "oi"));

        // Then the same glossary gains a new dictionary, no second glossary is created
        assertThat(id).isEqualTo("glossary-existing");
        deepL.verify(0, postRequestedFor(urlEqualTo("/v3/glossaries")));
        deepL.verify(putRequestedFor(urlPathEqualTo("/v3/glossaries/glossary-existing/dictionaries"))
                .withRequestBody(com.github.tomakehurst.wiremock.client.WireMock.containing("source_lang=en")));
    }

    @Test
    void cachesTheReferencePerBusinessAndPairWithoutListingAgain() {
        // Given
        var businessId = UUID.randomUUID();
        var name = "frappe-business-" + businessId;
        stubList(glossaryJson("glossary-existing", name, "2024-01-01T00:00:00Z", dictionaryJson("es", "pt")));
        deepL.stubFor(put(urlPathEqualTo("/v3/glossaries/glossary-existing/dictionaries"))
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody(dictionaryJson("es", "pt"))));
        var glossaries = glossaries();

        // When
        var first = glossaries.ensure(
                businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of("hola", "olá"));
        var second = glossaries.ensure(
                businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of("hola", "olá"));

        // Then only the first call touched the network
        assertThat(second).isEqualTo(first);
        deepL.verify(1, getRequestedFor(urlPathEqualTo("/v3/glossaries")));
        deepL.verify(1, putRequestedFor(urlPathEqualTo("/v3/glossaries/glossary-existing/dictionaries")));
    }

    @Test
    void keepsTheOldestSameNameGlossaryAndDeletesConcurrentDuplicatesAfterCreating() {
        // Given a race: by the time we re-list after creating, another instance's glossary (created earlier) is
        // also there
        var businessId = UUID.randomUUID();
        var name = "frappe-business-" + businessId;
        stubEmptyList();
        deepL.stubFor(post(urlPathEqualTo("/v3/glossaries"))
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody(glossaryJson(
                                "glossary-ours", name, "2024-01-01T00:00:02Z", dictionaryJson("es", "pt")))));
        // After creating, ensure() re-lists: both glossaries are now visible, ours is younger
        deepL.stubFor(get(urlPathEqualTo("/v3/glossaries"))
                .inScenario("race")
                .whenScenarioStateIs(com.github.tomakehurst.wiremock.stubbing.Scenario.STARTED)
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("{\"glossaries\":[]}"))
                .willSetStateTo("created"));
        deepL.stubFor(get(urlPathEqualTo("/v3/glossaries"))
                .inScenario("race")
                .whenScenarioStateIs("created")
                .willReturn(aResponse()
                        .withStatus(200)
                        .withHeader("Content-Type", "application/json")
                        .withBody("{\"glossaries\":["
                                + glossaryJson(
                                        "glossary-theirs", name, "2024-01-01T00:00:01Z", dictionaryJson("es", "pt"))
                                + ","
                                + glossaryJson(
                                        "glossary-ours", name, "2024-01-01T00:00:02Z", dictionaryJson("es", "pt"))
                                + "]}")));
        deepL.stubFor(delete(urlPathEqualTo("/v3/glossaries/glossary-ours"))
                .willReturn(aResponse().withStatus(204)));

        // When
        var id = glossaries()
                .ensure(businessId, SupportedLocales.SPANISH, SupportedLocales.PORTUGUESE, Map.of("hola", "olá"));

        // Then the older glossary (theirs) wins, and ours (the younger duplicate) is deleted
        assertThat(id).isEqualTo("glossary-theirs");
        deepL.verify(deleteRequestedFor(urlPathEqualTo("/v3/glossaries/glossary-ours")));
    }
}
