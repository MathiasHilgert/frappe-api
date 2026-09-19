package com.frappe.platform.infrastructure.i18n;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.i18n.TenantLocaleDefaults;
import com.frappe.platform.i18n.UserLocalePreference;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpHeaders;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.client.RestTestClient;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@AutoConfigureRestTestClient
@Import({
    TestcontainersConfiguration.class,
    TestNatsConfiguration.class,
    FailingLocalePortsIntegrationTests.FailingPorts.class
})
@ActiveProfiles("local")
class FailingLocalePortsIntegrationTests {

    @TestConfiguration(proxyBeanMethods = false)
    static class FailingPorts {

        @Bean
        UserLocalePreference failingUserPreference() {
            return request -> {
                throw new IllegalStateException("Identity is down");
            };
        }

        @Bean
        TenantLocaleDefaults failingTenantDefaults() {
            return request -> {
                throw new IllegalStateException("Organization is down");
            };
        }
    }

    @Autowired
    RestTestClient http;

    @Test
    void healthStaysUpAndLocalizedWhenTheLocalePortsFail() {
        // When / Then
        http.get()
                .uri("/actuator/health")
                .header(HttpHeaders.ACCEPT_LANGUAGE, "es-AR")
                .exchange()
                .expectStatus()
                .isOk()
                .expectHeader()
                .valueEquals(HttpHeaders.CONTENT_LANGUAGE, "es");
    }
}
