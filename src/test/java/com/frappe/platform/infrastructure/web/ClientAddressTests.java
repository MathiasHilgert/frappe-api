package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import jakarta.servlet.http.HttpServletRequest;
import org.junit.jupiter.api.Nested;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.TestPropertySource;
import org.springframework.test.web.servlet.client.RestTestClient;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * The client address comes from {@code X-Forwarded-For} only when the connection comes from a trusted proxy
 * ({@code FRAPPE_TRUSTED_PROXIES}, loopback by default); the test client connects over loopback.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@AutoConfigureRestTestClient
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, ClientAddressTests.Routes.class})
@ActiveProfiles("local")
class ClientAddressTests {

    static final String FORWARDED_FOR = "X-Forwarded-For";
    static final String CLIENT = "198.51.100.23";
    static final String LOOPBACK_V4 = "127.0.0.1";
    static final String LOOPBACK_V6 = "0:0:0:0:0:0:0:1";

    @TestConfiguration(proxyBeanMethods = false)
    static class Routes {

        @Bean
        ClientAddressRoute clientAddressRoute() {
            return new ClientAddressRoute();
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class ClientAddressRoute {

        @GetMapping("/test/client-address")
        String answer(HttpServletRequest request) {
            return request.getRemoteAddr();
        }
    }

    @Autowired
    RestTestClient http;

    @Test
    void theAddressForwardedByATrustedProxyIsTheClientAddress() {
        assertThat(clientAddress(http, CLIENT)).isEqualTo(CLIENT);
    }

    @Test
    void addressesTheCallerPrependedAreNotTrusted() {
        // Given a caller that sent its own X-Forwarded-For, which the proxy appends to
        var forwarded = "203.0.113.9, " + CLIENT;

        // Then the address the trusted proxy saw wins, not the one the caller claims
        assertThat(clientAddress(http, forwarded)).isEqualTo(CLIENT);
    }

    @Nested
    @TestPropertySource(properties = "FRAPPE_TRUSTED_PROXIES=10.0.0.0/8")
    class WhenThePeerIsNoTrustedProxy {

        @Autowired
        RestTestClient untrustedPeer;

        @Test
        void theForwardedAddressIsIgnored() {
            assertThat(clientAddress(untrustedPeer, CLIENT)).isIn(LOOPBACK_V4, LOOPBACK_V6);
        }
    }

    static String clientAddress(RestTestClient client, String forwardedFor) {
        return client.get()
                .uri("/v1/test/client-address")
                .header(FORWARDED_FOR, forwardedFor)
                .exchange()
                .expectStatus()
                .isOk()
                .expectBody(String.class)
                .returnResult()
                .getResponseBody();
    }
}
