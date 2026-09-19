package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.i18n.UserLocalePreference;
import java.util.Optional;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.context.i18n.LocaleContextHolder;
import org.springframework.http.HttpHeaders;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.client.RestTestClient;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@AutoConfigureRestTestClient
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, LocaleResolutionIntegrationTests.Probe.class})
@ActiveProfiles("local")
class LocaleResolutionIntegrationTests {

    static final String PROBE_PATH = "/test/locale-probe";
    static final String FAILING_PATH = "/test/locale-probe/failure";
    static final String SIGNED_IN_AS_PORTUGUESE_SPEAKER = "X-Test-Portuguese-User";

    @TestConfiguration(proxyBeanMethods = false)
    static class Probe {

        @Bean
        UserLocalePreference portugueseSpeakingTestUser() {
            return request -> request.getHeader(SIGNED_IN_AS_PORTUGUESE_SPEAKER) != null
                    ? Optional.of(SupportedLocales.PORTUGUESE)
                    : Optional.empty();
        }

        @Bean
        LocaleProbeController localeProbeController() {
            return new LocaleProbeController();
        }
    }

    @RestController
    static class LocaleProbeController {

        @GetMapping(PROBE_PATH)
        String locale() {
            return LocaleContextHolder.getLocale().toLanguageTag();
        }

        @GetMapping(FAILING_PATH)
        String failure() {
            throw new IllegalStateException("Probe failure");
        }
    }

    @Autowired
    RestTestClient http;

    @Test
    void answersARegionalAcceptLanguageInItsLanguageAndVariesByIt() {
        // When
        var result = http.get()
                .uri(PROBE_PATH)
                .header(HttpHeaders.ACCEPT_LANGUAGE, "es-AR")
                .exchange()
                .expectStatus()
                .isOk()
                .expectBody(String.class)
                .returnResult();

        // Then
        assertThat(result.getResponseBody()).isEqualTo("es");
        assertThat(result.getResponseHeaders().getFirst(HttpHeaders.CONTENT_LANGUAGE))
                .isEqualTo("es");
        assertThat(result.getResponseHeaders().getVary()).contains(HttpHeaders.ACCEPT_LANGUAGE);
    }

    @Test
    void answersInEnglishWithoutAcceptLanguage() {
        // When / Then
        http.get()
                .uri(PROBE_PATH)
                .exchange()
                .expectHeader()
                .valueEquals(HttpHeaders.CONTENT_LANGUAGE, "en")
                .expectBody(String.class)
                .isEqualTo("en");
    }

    @Test
    void theSignedInUsersPreferenceWinsOverAcceptLanguage() {
        // When / Then
        http.get()
                .uri(PROBE_PATH)
                .header(HttpHeaders.ACCEPT_LANGUAGE, "es")
                .header(SIGNED_IN_AS_PORTUGUESE_SPEAKER, "true")
                .exchange()
                .expectHeader()
                .valueEquals(HttpHeaders.CONTENT_LANGUAGE, "pt")
                .expectBody(String.class)
                .isEqualTo("pt");
    }

    @Test
    void announcesTheLanguageOnErrorResponsesToo() {
        // When / Then
        for (var path : new String[] {"/test/locale-probe/missing", FAILING_PATH}) {
            var result = http.get()
                    .uri(path)
                    .header(HttpHeaders.ACCEPT_LANGUAGE, "pt-BR")
                    .exchange()
                    .expectBody(String.class)
                    .returnResult();

            assertThat(result.getStatus().isError()).as(path).isTrue();
            assertThat(result.getResponseHeaders().getFirst(HttpHeaders.CONTENT_LANGUAGE))
                    .as(path)
                    .isEqualTo("pt");
            assertThat(result.getResponseHeaders().getVary()).as(path).containsOnlyOnce(HttpHeaders.ACCEPT_LANGUAGE);
        }
    }
}
